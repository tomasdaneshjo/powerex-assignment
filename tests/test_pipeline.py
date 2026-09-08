import pytest
from lxml import etree

from powerex_scraper.pipeline import (TransformError, run_pipeline,
                                        table_to_csv, to_xml)

XSLT_DIR = "src/powerex_scraper/xslt"
TEST_XSLT_DIR = "tests/fixtures/xslt"


def test_live_perunit_fixture_full_pipeline_parity(load_fixture_bytes):
    csv_text, transformed = run_pipeline(
        load_fixture_bytes("live_a73_perunit.xml"), "xml",
        f"{XSLT_DIR}/entsoe-perunit.xslt")
    lines = [l for l in csv_text.split("\r\n") if l]
    assert lines[0] == ("timestamp_utc,resolution,area,unit_eic,unit_name,"
                        "production_type,direction,quantity_mw")
    assert len(lines) - 1 == 313          # 29 sparse A03 series (both directions)
    assert any("24WG--EBOG31---D" in l and "236.756" in l and "generation" in l
               for l in lines)
    assert any("Čierny Váh TG1" in l and "consumption" in l for l in lines)
    assert lines[1].startswith("2026-09-03T22:00:00Z,PT60M,")  # EXSLT date math
    assert b"<table>" in transformed


def test_live_forecast_fixture_direction_is_explicit(load_fixture_bytes):
    csv_text, _ = run_pipeline(
        load_fixture_bytes("live_a71_dayahead.xml"), "xml",
        f"{XSLT_DIR}/entsoe-forecast.xslt")
    lines = [l for l in csv_text.split("\r\n") if l]
    assert lines[0] == "timestamp_utc,resolution,area,kind,quantity_mw"
    assert len(lines) - 1 == 24
    # the native XML states the direction; no positional slot semantics assumed
    assert lines[1] == ("2026-09-03T22:00:00Z,PT60M,10YSK-SEPS-----K,"
                        "generation_forecast,2780.4")
    assert lines[-1].startswith("2026-09-04T21:00:00Z")


def test_json_mode_end_to_end_through_the_canonical_mapping(load_fixture_bytes):
    csv_text, _ = run_pipeline(load_fixture_bytes("tiny.json"), "json",
                               f"{TEST_XSLT_DIR}/tiny-json-table.xslt")
    assert csv_text.split("\r\n")[:3] == ["id,value", "a,1", "b,2"]


def test_xml_responses_pass_through_without_conversion(load_fixture_bytes):
    csv_text, _ = run_pipeline(load_fixture_bytes("tiny.xml"), "xml",
                               f"{TEST_XSLT_DIR}/tiny-table.xslt")
    assert csv_text.split("\r\n")[:3] == ["id,value", "a,1", "b,2"]


def test_json_declared_but_not_json_is_a_transform_error():
    with pytest.raises(TransformError, match="not valid JSON"):
        run_pipeline(b"<xml/>", "json", f"{TEST_XSLT_DIR}/tiny-table.xslt")


def test_xml_declared_but_malformed_is_a_transform_error():
    with pytest.raises(TransformError, match="not well-formed"):
        run_pipeline(b"{not xml}", "xml", f"{TEST_XSLT_DIR}/tiny-table.xslt")


def test_non_table_output_fails_the_xsd_gate_and_carries_the_document(load_fixture_bytes):
    with pytest.raises(TransformError, match="not CSV-convertible") as exc_info:
        run_pipeline(load_fixture_bytes("tiny.xml"), "xml",
                     f"{TEST_XSLT_DIR}/not-a-table.xslt")
    assert b"<wrong>" in exc_info.value.transformed  # archived for diagnosis


def test_table_to_csv_unions_columns_in_encounter_order():
    table = etree.fromstring(
        '<table><row><cell name="a">1</cell></row>'
        '<row><cell name="a">2</cell><cell name="b">x,y</cell></row></table>')
    assert table_to_csv(table).split("\r\n")[:3] == ["a,b", "1,", '2,"x,y"']


def test_empty_table_is_valid_and_yields_header_only():
    csv_text = table_to_csv(etree.fromstring("<table/>"))
    assert csv_text == "\r\n"


def test_to_xml_json_produces_canonical_root():
    assert to_xml(b'{"a": 1}', "json").tag == "root"


def test_external_entities_in_responses_are_not_resolved(tmp_path):
    """XXE: a response declaring a SYSTEM entity must never leak file content
    into the pipeline output."""
    secret = tmp_path / "secret.txt"
    secret.write_text("TOP-SECRET")
    xxe = (f'<?xml version="1.0"?><!DOCTYPE data [<!ENTITY x SYSTEM '
           f'"file://{secret}">]><data><entry id="a">&x;</entry></data>').encode()
    try:
        csv_text, _ = run_pipeline(xxe, "xml", f"{TEST_XSLT_DIR}/tiny-table.xslt")
        assert "TOP-SECRET" not in csv_text
    except TransformError:
        pass  # rejecting the document outright is equally acceptable


def test_stylesheet_file_access_is_denied(tmp_path):
    """Defense in depth: even a stylesheet using document() cannot read files
    under the DENY_ALL access control."""
    leaky = tmp_path / "leaky.xslt"
    leaky.write_text(
        '<?xml version="1.0"?>'
        '<xsl:stylesheet version="1.0" xmlns:xsl="http://www.w3.org/1999/XSL/Transform">'
        '<xsl:template match="/"><table><row>'
        '<cell name="x"><xsl:value-of select="document(\'/etc/hosts\')"/></cell>'
        "</row></table></xsl:template></xsl:stylesheet>")
    with pytest.raises(TransformError):
        run_pipeline(b"<data/>", "xml", leaky)


def test_duplicate_cell_names_in_a_row_fail_certification():
    table = etree.fromstring(
        '<table><row><cell name="a">1</cell><cell name="a">2</cell></row></table>')
    with pytest.raises(TransformError, match="duplicate cell name"):
        table_to_csv(table)


def test_any_post_transform_failure_carries_the_document(tmp_path):
    # duplicate cell names surface in CSV conversion, after the XSD gate —
    # the exception must still carry the transformed doc for failed/ archiving
    style = tmp_path / "dup.xslt"
    style.write_text("""<xsl:stylesheet version="1.0"
        xmlns:xsl="http://www.w3.org/1999/XSL/Transform">
      <xsl:template match="/"><table><row>
        <cell name="a">1</cell><cell name="a">2</cell>
      </row></table></xsl:template>
    </xsl:stylesheet>""")
    with pytest.raises(TransformError, match="duplicate cell name") as exc_info:
        run_pipeline(b"<data/>", "xml", style)
    assert b'<cell name="a">' in exc_info.value.transformed


NS = "urn:iec62325.351:tc57wg16:451-6:generationloaddocument:3:0"


def _gl_doc(resolution: str, positions=(1, 2)) -> bytes:
    points = "".join(
        f"<Point><position>{p}</position><quantity>{p * 10}</quantity></Point>"
        for p in positions)
    return (f'<GL_MarketDocument xmlns="{NS}"><TimeSeries>'
            f"<inBiddingZone_Domain.mRID>ZONE</inBiddingZone_Domain.mRID>"
            f"<Period><timeInterval><start>2026-09-03T22:00Z</start>"
            f"<end>2026-09-05T22:00Z</end></timeInterval>"
            f"<resolution>{resolution}</resolution>{points}</Period>"
            f"</TimeSeries></GL_MarketDocument>").encode()


@pytest.mark.parametrize("resolution,second_ts", [
    ("PT15M", "2026-09-03T22:15:00Z"),
    ("PT60M", "2026-09-03T23:00:00Z"),
    ("PT1H", "2026-09-03T23:00:00Z"),
    ("P1D", "2026-09-04T22:00:00Z"),
    ("PT0.5S", "2026-09-03T22:00:00.5Z"),  # fractional seconds — libxslt preserves them
    ("P1M", "2026-10-03T22:00:00Z"),    # calendar month — scaled as months
    ("P1Y", "2027-09-03T22:00:00Z"),    # calendar year — 12 months
    ("P1Y6M", "2028-03-03T22:00:00Z"),  # 18 months
])
def test_resolutions_are_general_iso_durations(resolution, second_ts):
    csv_text, _ = run_pipeline(_gl_doc(resolution), "xml",
                               f"{XSLT_DIR}/entsoe-forecast.xslt")
    lines = csv_text.split("\r\n")
    assert lines[1].startswith("2026-09-03T22:00:00Z")
    assert lines[2].startswith(second_ts)


def test_mixed_calendar_time_resolution_fails_loudly():
    # P1M1D cannot be scalar-multiplied in any calendar (XPath 2.0 forbids
    # xs:duration × number for the same reason)
    with pytest.raises(TransformError):
        run_pipeline(_gl_doc("P1M1D"), "xml", f"{XSLT_DIR}/entsoe-forecast.xslt")
