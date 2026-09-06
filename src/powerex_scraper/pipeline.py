"""The transformation pipeline: bytes → XML → XSLT → XSD gate → CSV.

The single table.xsd is the machine-checkable definition of "convertible to
CSV": whatever an endpoint's stylesheet produces must validate against it
before any CSV is written. Column order is the encounter order of cell names
across rows (document order).
"""
from __future__ import annotations

import csv
import io
import json
from typing import NamedTuple

from lxml import etree

from .config import TABLE_XSD, ResponseFormat
from .json2xml import json_to_xml


class TransformError(ValueError):
    """The response could not be parsed, transformed, or certified as a table.

    ``transformed`` carries the XSLT output when the failure happened after the
    transform (gate rejection, CSV conversion) so the caller can archive it."""

    def __init__(self, message: str, transformed: bytes | None = None):
        super().__init__(message)
        self.transformed = transformed


# Untrusted-input hardening: API responses are parsed with entity resolution
# and DTD loading OFF (classic lxml XXE: default parsing resolves external
# entities from the local filesystem), and stylesheets run under DENY_ALL —
# no document()/exsl:document file or network access, even though the shipped
# stylesheets are trusted. EXSLT date arithmetic needs no I/O and still works.
_UNTRUSTED_PARSER = etree.XMLParser(resolve_entities=False, load_dtd=False,
                                    no_network=True)
_DENY_ALL = etree.XSLTAccessControl.DENY_ALL

_schema: etree.XMLSchema | None = None


def _table_schema() -> etree.XMLSchema:
    global _schema
    if _schema is None:
        _schema = etree.XMLSchema(etree.parse(str(TABLE_XSD)))
    return _schema


def to_xml(raw: bytes, response_format: ResponseFormat) -> etree._Element:
    """Raw response bytes → an XML tree (converting from JSON when declared)."""
    if response_format == "json":
        try:
            return json_to_xml(json.loads(raw))
        except json.JSONDecodeError as exc:
            raise TransformError(f"response declared as json is not valid JSON: {exc}") from exc
    try:
        return etree.fromstring(raw, parser=_UNTRUSTED_PARSER)
    except etree.XMLSyntaxError as exc:
        raise TransformError(f"response declared as xml is not well-formed: {exc}") from exc


def apply_xslt(tree: etree._Element, xslt_path) -> etree._Element:
    try:
        stylesheet = etree.XSLT(etree.parse(str(xslt_path)),
                                access_control=_DENY_ALL)
    except (etree.XMLSyntaxError, etree.XSLTParseError) as exc:
        raise TransformError(f"stylesheet {xslt_path} is invalid: {exc}") from exc
    try:
        return stylesheet(tree).getroot()
    except etree.XSLTApplyError as exc:
        raise TransformError(f"stylesheet {xslt_path} failed: {exc}") from exc


def validate_table(tree: etree._Element) -> None:
    schema = _table_schema()
    if not schema.validate(tree):
        detail = "; ".join(str(e.message) for e in schema.error_log[:3])
        raise TransformError(
            f"transformed document is not CSV-convertible (table.xsd): {detail}")


def table_to_csv(tree: etree._Element) -> str:
    columns: list[str] = []
    rows: list[dict[str, str]] = []
    for row in tree.iter("row"):
        record: dict[str, str] = {}
        for cell in row.iter("cell"):
            name = cell.get("name")
            if name in record:
                raise TransformError(
                    f"duplicate cell name '{name}' within one row — the "
                    "stylesheet emitted ambiguous columns")
            if name not in columns:
                columns.append(name)
            record[name] = cell.text or ""
        rows.append(record)

    buffer = io.StringIO()
    writer = csv.DictWriter(buffer, fieldnames=columns, restval="",
                            lineterminator="\r\n")
    writer.writeheader()
    writer.writerows(rows)
    return buffer.getvalue()


class PipelineResult(NamedTuple):
    csv_text: str
    transformed_xml: bytes  # kept so the caller can archive the certified doc


def run_pipeline(raw: bytes, response_format: ResponseFormat, xslt_path) -> PipelineResult:
    tree = to_xml(raw, response_format)
    table = apply_xslt(tree, xslt_path)
    transformed = etree.tostring(table, pretty_print=True)
    try:
        validate_table(table)
        csv_text = table_to_csv(table)
    except TransformError as exc:
        exc.transformed = transformed  # for the caller to archive and diagnose
        raise
    return PipelineResult(csv_text, transformed)
