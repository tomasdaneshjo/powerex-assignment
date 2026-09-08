"""Endpoint configuration for the universal API→CSV scraper.

An *endpoint* is one declared HTTP request plus its transformation contract:

    {
      "baseUrl": "https://api.example.com",
      "endpoints": [{
        "id": "my-dataset",
        "path": "/v1/data",                  # joined onto baseUrl (or absolute "url")
        "method": "GET",                     # default GET; body allowed for POST/PUT
        "query":   { "token": "${env:API_KEY}", "from": "${date:%Y%m%d:-P1D}" },
        "headers": { "Accept": "application/json" },
        "body": "...optional, placeholders apply...",
        "responseFormat": "json",            # "json" (converted to XML) | "xml"
        "xslt": "my-dataset.xslt",           # stylesheet in the xslt/ folder
        "timezone": "Europe/Bratislava",     # anchor timezone for date placeholders
        "keyDateOffsetDays": -1              # names the output objects (default 0)
      }]
    }

Placeholders (see placeholders.py) work in url/path, query values, header
values, and the body. The XSLT must produce the canonical table document
validated by schema/table.xsd — that validation is what certifies a response
as CSV-convertible.
"""
from __future__ import annotations

import json
import os
from dataclasses import dataclass, field
from pathlib import Path
from typing import Literal, get_args
from zoneinfo import ZoneInfo, ZoneInfoNotFoundError

PACKAGE_DIR = Path(__file__).parent
XSLT_DIR = PACKAGE_DIR / "xslt"
TABLE_XSD = PACKAGE_DIR / "schema" / "table.xsd"

ResponseFormat = Literal["json", "xml"]
RESPONSE_FORMATS = get_args(ResponseFormat)  # runtime check derives from the type


class ConfigError(ValueError):
    """endpoints.json or environment is invalid — named, actionable message."""


@dataclass(frozen=True)
class Endpoint:
    id: str
    url: str                       # fully joined, still containing placeholders
    method: str
    query: dict[str, str]
    headers: dict[str, str]
    body: str | None
    response_format: ResponseFormat
    xslt_path: Path
    timezone: str
    key_date_offset_days: int


@dataclass(frozen=True)
class AppConfig:
    bucket: str
    endpoints: list[Endpoint] = field(default_factory=list)


def _require(mapping: dict, key: str, where: str):
    if key not in mapping or mapping[key] in (None, "", [], {}):
        raise ConfigError(f"{where}: missing required field '{key}'")
    return mapping[key]


def _string_map(value, where: str) -> dict[str, str]:
    if value is None:
        return {}
    if not isinstance(value, dict):
        raise ConfigError(f"{where}: must be an object of strings")
    return {str(k): str(v) for k, v in value.items()}


def _endpoint_url(entry: dict, base_url: str, where: str) -> str:
    if entry.get("url"):
        return str(entry["url"])
    if not base_url:
        raise ConfigError(f"{where}: needs 'url', or 'path' with a top-level 'baseUrl'")
    return base_url + "/" + str(_require(entry, "path", where)).lstrip("/")


def _endpoint_xslt(entry: dict, xslt_dir: Path, where: str) -> Path:
    xslt_name = str(_require(entry, "xslt", where))
    xslt_path = xslt_dir / xslt_name
    if not xslt_path.is_file():
        raise ConfigError(f"{where}: stylesheet '{xslt_name}' not found in {xslt_dir}")
    return xslt_path


def _parse_endpoint(entry: dict, index: int, base_url: str, xslt_dir: Path) -> Endpoint:
    where = f"endpoints[{index}]"
    ep_id = _require(entry, "id", where)
    where = f"endpoint '{ep_id}'"

    response_format = str(_require(entry, "responseFormat", where)).lower()
    if response_format not in RESPONSE_FORMATS:
        raise ConfigError(f"{where}: responseFormat '{response_format}' "
                          f"(expected one of {list(RESPONSE_FORMATS)})")

    method = str(entry.get("method", "GET")).upper()
    body = entry.get("body")
    if body is not None and method == "GET":
        raise ConfigError(f"{where}: 'body' is not allowed with method GET")

    tz_name = str(entry.get("timezone", "UTC"))
    try:
        ZoneInfo(tz_name)
    except (ZoneInfoNotFoundError, ValueError) as exc:
        raise ConfigError(f"{where}: unknown timezone '{tz_name}'") from exc
    try:
        key_offset = int(entry.get("keyDateOffsetDays", 0))
    except (TypeError, ValueError) as exc:
        raise ConfigError(f"{where}: keyDateOffsetDays must be an integer") from exc

    return Endpoint(
        id=ep_id,
        url=_endpoint_url(entry, base_url, where),
        method=method,
        query=_string_map(entry.get("query"), f"{where} query"),
        headers=_string_map(entry.get("headers"), f"{where} headers"),
        body=str(body) if body is not None else None,
        response_format=response_format,
        xslt_path=_endpoint_xslt(entry, xslt_dir, where),
        timezone=tz_name,
        key_date_offset_days=key_offset,
    )


def load_config(endpoints_path: str | Path | None = None,
                env: dict[str, str] | None = None,
                xslt_dir: str | Path | None = None) -> AppConfig:
    env = os.environ if env is None else env
    path = Path(endpoints_path) if endpoints_path else PACKAGE_DIR / "endpoints.json"
    xslt_dir = Path(xslt_dir) if xslt_dir else XSLT_DIR
    try:
        doc = json.loads(path.read_text(encoding="utf-8"))
    except json.JSONDecodeError as exc:
        raise ConfigError(f"{path}: not valid JSON: {exc}") from exc

    base_url = str(doc.get("baseUrl", "")).rstrip("/")
    endpoints = [_parse_endpoint(entry, i, base_url, xslt_dir)
                 for i, entry in enumerate(_require(doc, "endpoints", str(path)))]

    seen: set[str] = set()
    for endpoint in endpoints:
        if endpoint.id in seen:
            raise ConfigError(f"endpoint '{endpoint.id}': duplicate id")
        seen.add(endpoint.id)

    return AppConfig(bucket=env.get("OUTPUT_BUCKET", ""), endpoints=endpoints)
