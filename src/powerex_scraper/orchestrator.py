"""Runs the configured endpoints with per-endpoint failure isolation.

Artifact policy per (endpoint, run):
    raw/{id}/{key-date}.{json|xml}     — the response bytes, archived BEFORE any
                                         transformation (also on fetch errors
                                         that carried a body)
    transformed/{id}/{key-date}.xml    — the XSLT output that passed the
                                         table.xsd gate (the certified table)
    data/{id}/{key-date}.csv           — the certified CSV
    failed/{id}/{key-date}.xml         — the XSLT output that FAILED the gate
                                         (kept for diagnosis)

Keys carry the anchor date shifted by the endpoint's keyDateOffsetDays, so
retries and backfills overwrite idempotently.
"""
from __future__ import annotations

from dataclasses import asdict, dataclass
from datetime import date, timedelta

from . import http
from .config import AppConfig, Endpoint
from .pipeline import TransformError, run_pipeline
from .placeholders import PlaceholderError, resolve_anchor
from .storage import Store


@dataclass(frozen=True)
class JobResult:
    endpoint_id: str
    success: bool
    row_count: int = 0
    error: str | None = None


@dataclass(frozen=True)
class RunSummary:
    results: list[JobResult]

    @property
    def failed(self) -> list[JobResult]:
        return [r for r in self.results if not r.success]

    def to_dict(self) -> dict:
        return {"results": [asdict(r) for r in self.results]}


class ScrapeFailedError(RuntimeError):
    def __init__(self, summary: RunSummary):
        failures = "; ".join(f"{r.endpoint_id}: {r.error}" for r in summary.failed)
        super().__init__(
            f"{len(summary.failed)} of {len(summary.results)} endpoints failed: {failures}")
        self.summary = summary


def _key_date(endpoint: Endpoint, anchor: date) -> str:
    return (anchor + timedelta(days=endpoint.key_date_offset_days)).isoformat()


def _scrape(endpoint: Endpoint, store: Store, env: dict[str, str],
            anchor_date: date | None, fetch, log) -> JobResult:
    """Fetch → archive raw → pipeline → store outputs, for one endpoint."""
    anchor = resolve_anchor(endpoint.timezone, anchor_date)
    key_date = _key_date(endpoint, anchor)
    raw_ext = "json" if endpoint.response_format == "json" else "xml"
    raw_key = f"raw/{endpoint.id}/{key_date}.{raw_ext}"
    raw_type = "application/json" if raw_ext == "json" else "application/xml"

    try:
        raw = fetch(endpoint, env, anchor)
    except http.ApiError as exc:
        if exc.raw_body:  # a response WAS received — archive it anyway
            store.put(raw_key, exc.raw_body, raw_type)
        raise
    store.put(raw_key, raw, raw_type)

    try:
        csv_text, transformed = run_pipeline(
            raw, endpoint.response_format, endpoint.xslt_path)
    except TransformError as exc:
        if exc.transformed:
            store.put(f"failed/{endpoint.id}/{key_date}.xml",
                      exc.transformed, "application/xml")
        raise

    store.put(f"transformed/{endpoint.id}/{key_date}.xml",
              transformed, "application/xml")
    store.put(f"data/{endpoint.id}/{key_date}.csv",
              csv_text.encode("utf-8"), "text/csv")
    rows = max(csv_text.count("\r\n") - 1, 0)
    log(f"endpoint={endpoint.id} keyDate={key_date} rows={rows}")
    return JobResult(endpoint.id, True, rows)


def run(config: AppConfig, store: Store, env: dict[str, str],
        endpoint_ids: list[str] | None = None,
        anchor_date: date | None = None, fetch=None, log=print) -> RunSummary:
    """``endpoint_ids`` None or empty selects every configured endpoint."""
    fetch = fetch or http.fetch
    selected = (config.endpoints if not endpoint_ids
                else [e for e in config.endpoints if e.id in endpoint_ids])
    unknown = set(endpoint_ids or []) - {e.id for e in selected}

    results = [JobResult(endpoint_id=name, success=False,
                         error=f"unknown endpoint id '{name}'")
               for name in sorted(unknown)]
    for endpoint in selected:
        try:
            results.append(_scrape(endpoint, store, env, anchor_date, fetch, log))
        except Exception as exc:  # noqa: BLE001 — isolation is the contract:
            # storage-layer errors (botocore), bad timezones, and anything
            # unforeseen must fail THIS endpoint, never abort the run
            log(f"endpoint={endpoint.id} FAILED: {exc}")
            results.append(JobResult(endpoint.id, False, error=str(exc)))

    return RunSummary(results=results)
