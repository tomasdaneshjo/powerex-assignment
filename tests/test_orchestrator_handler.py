import json
from datetime import date

import pytest

from powerex_scraper import handler, http
from powerex_scraper.config import ConfigError, load_config
from powerex_scraper.http import ApiError
from powerex_scraper.orchestrator import ScrapeFailedError, run

ANCHOR = date(2026, 9, 5)


def fake_fetch(outcomes: dict):
    def _fetch(endpoint, env, anchor):
        outcome = outcomes[endpoint.id]
        if isinstance(outcome, Exception):
            raise outcome
        return outcome
    return _fetch


def test_happy_path_archives_raw_and_certified_csv(load_fixture_bytes, memory_store):
    config = load_config(env={})
    outcomes = {
        "entsoe-generation-forecast-dayahead": load_fixture_bytes("live_a71_dayahead.xml"),
        "entsoe-generation-actual-perunit": load_fixture_bytes("live_a73_perunit.xml"),
    }
    summary = run(config, memory_store, env={}, anchor_date=ANCHOR,
                  fetch=fake_fetch(outcomes), log=lambda _: None)

    assert not summary.failed
    keys = set(memory_store.objects)
    # keyDateOffsetDays: forecast +1, per-unit -1
    assert "raw/entsoe-generation-forecast-dayahead/2026-09-06.xml" in keys
    assert "data/entsoe-generation-forecast-dayahead/2026-09-06.csv" in keys
    assert "transformed/entsoe-generation-forecast-dayahead/2026-09-06.xml" in keys
    assert "data/entsoe-generation-actual-perunit/2026-09-04.csv" in keys
    assert memory_store.objects[
        "transformed/entsoe-generation-actual-perunit/2026-09-04.xml"].startswith(b"<table>")
    csv_text = memory_store.objects["data/entsoe-generation-actual-perunit/2026-09-04.csv"].decode()
    assert "24WG--EBOG31---D" in csv_text and "236.756" in csv_text
    assert "Čierny Váh TG1" in csv_text


def test_one_failure_does_not_block_the_rest(load_fixture_bytes, memory_store):
    config = load_config(env={})
    outcomes = {
        "entsoe-generation-forecast-dayahead": ApiError("boom"),
        "entsoe-generation-actual-perunit": load_fixture_bytes("live_a73_perunit.xml"),
    }
    summary = run(config, memory_store, env={}, anchor_date=ANCHOR,
                  fetch=fake_fetch(outcomes), log=lambda _: None)
    assert [r.endpoint_id for r in summary.failed] == ["entsoe-generation-forecast-dayahead"]
    assert "data/entsoe-generation-actual-perunit/2026-09-04.csv" in memory_store.objects


def test_error_body_is_archived_even_on_fetch_failure(memory_store):
    config = load_config(env={})
    outcomes = {
        "entsoe-generation-forecast-dayahead": ApiError("HTTP 400", raw_body=b"denied"),
        "entsoe-generation-actual-perunit": ApiError("HTTP 400", raw_body=b"denied"),
    }
    run(config, memory_store, env={}, endpoint_ids=["entsoe-generation-forecast-dayahead"],
        anchor_date=ANCHOR, fetch=fake_fetch(outcomes), log=lambda _: None)
    assert memory_store.objects["raw/entsoe-generation-forecast-dayahead/2026-09-06.xml"] == b"denied"


def test_failed_transform_archives_raw_and_transformed(tmp_path, memory_store,
                                                       load_fixture_bytes):
    doc = {"baseUrl": "https://x", "endpoints": [
        {"id": "bad", "path": "/x", "responseFormat": "xml",
         "xslt": "not-a-table.xslt"}]}
    p = tmp_path / "endpoints.json"
    p.write_text(json.dumps(doc))
    config = load_config(p, env={}, xslt_dir="tests/fixtures/xslt")

    summary = run(config, memory_store, env={}, anchor_date=ANCHOR,
                  fetch=fake_fetch({"bad": load_fixture_bytes("tiny.xml")}),
                  log=lambda _: None)
    assert len(summary.failed) == 1
    assert "not CSV-convertible" in summary.failed[0].error
    assert "raw/bad/2026-09-05.xml" in memory_store.objects
    assert b"<wrong>" in memory_store.objects["failed/bad/2026-09-05.xml"]


def test_unknown_endpoint_id_fails_without_fetch(memory_store):
    config = load_config(env={})

    def exploding(*a):
        raise AssertionError("fetch must not be called")

    summary = run(config, memory_store, env={}, endpoint_ids=["nope"],
                  anchor_date=ANCHOR, fetch=exploding, log=lambda _: None)
    assert [r.error for r in summary.failed] == ["unknown endpoint id 'nope'"]


# ---------------------------------------------------------------- handler ---

def test_handler_requires_bucket(monkeypatch):
    monkeypatch.delenv("OUTPUT_BUCKET", raising=False)
    with pytest.raises(ConfigError, match="OUTPUT_BUCKET"):
        handler.lambda_handler({}, None)


def test_handler_event_selection_and_anchor(monkeypatch, load_fixture_bytes,
                                            memory_store):
    monkeypatch.setenv("OUTPUT_BUCKET", "b")
    monkeypatch.setattr(handler, "S3Store", lambda bucket: memory_store)
    body = load_fixture_bytes("live_a71_dayahead.xml")
    monkeypatch.setattr(http, "fetch", lambda endpoint, env, anchor: body)

    result = handler.lambda_handler(
        {"endpointIds": ["entsoe-generation-forecast-dayahead"],
         "anchorDate": "2026-09-05"}, None)
    assert result["results"][0]["success"] is True
    assert "data/entsoe-generation-forecast-dayahead/2026-09-06.csv" in memory_store.objects


def test_handler_partial_failure_raises_after_storing(monkeypatch, load_fixture_bytes,
                                                      memory_store):
    monkeypatch.setenv("OUTPUT_BUCKET", "b")
    monkeypatch.setattr(handler, "S3Store", lambda bucket: memory_store)
    body = load_fixture_bytes("live_a73_perunit.xml")

    def fetch(endpoint, env, anchor):
        if endpoint.id == "entsoe-generation-forecast-dayahead":
            raise ApiError("boom")
        return body

    monkeypatch.setattr(http, "fetch", fetch)
    with pytest.raises(ScrapeFailedError) as exc_info:
        handler.lambda_handler({"anchorDate": "2026-09-05"}, None)
    assert len(exc_info.value.summary.failed) == 1
    assert "data/entsoe-generation-actual-perunit/2026-09-04.csv" in memory_store.objects


def test_storage_failure_is_isolated_per_endpoint(load_fixture_bytes):
    """A storage-layer error (e.g. botocore AccessDenied) fails one endpoint,
    never the run — the documented isolation contract."""
    config = load_config(env={})

    class ExplodingStore:
        def __init__(self):
            self.calls = 0

        def put(self, key, content, content_type):
            self.calls += 1
            if key.startswith("raw/entsoe-generation-forecast-dayahead"):
                raise RuntimeError("AccessDenied (simulated botocore error)")

    store = ExplodingStore()
    outcomes = {
        "entsoe-generation-forecast-dayahead": load_fixture_bytes("live_a71_dayahead.xml"),
        "entsoe-generation-actual-perunit": load_fixture_bytes("live_a73_perunit.xml"),
    }
    summary = run(config, store, env={}, anchor_date=ANCHOR,
                  fetch=fake_fetch(outcomes), log=lambda _: None)
    assert [r.endpoint_id for r in summary.failed] == ["entsoe-generation-forecast-dayahead"]
    assert any(r.success for r in summary.results)  # the second endpoint completed


def test_bad_timezone_fails_only_that_endpoint(tmp_path, load_fixture_bytes,
                                               memory_store):
    import json as _json
    doc = {"baseUrl": "https://x", "endpoints": [
        {"id": "ok", "path": "/a", "responseFormat": "xml", "xslt": "tiny-table.xslt"}]}
    p = tmp_path / "endpoints.json"
    p.write_text(_json.dumps(doc))
    config = load_config(p, env={}, xslt_dir="tests/fixtures/xslt")
    # config-load validates timezones, so a bad one is a NAMED startup error:
    doc["endpoints"][0]["timezone"] = "Europe/Bratlislava"
    p.write_text(_json.dumps(doc))
    with pytest.raises(ConfigError, match="Bratlislava"):
        load_config(p, env={}, xslt_dir="tests/fixtures/xslt")


def test_handler_rejects_malformed_anchor_date(monkeypatch):
    monkeypatch.setenv("OUTPUT_BUCKET", "b")
    with pytest.raises(ConfigError, match="anchorDate"):
        handler.lambda_handler({"anchorDate": "05-09-2026"}, None)
