import io
import json
import urllib.error
import urllib.parse
from datetime import date

import pytest

from powerex_scraper import http
from powerex_scraper.config import ConfigError, load_config
from powerex_scraper.http import ApiError, build_request, fetch

ANCHOR = date(2026, 9, 5)


# ---------------------------------------------------------------- config ----

def test_packaged_endpoints_load():
    config = load_config(env={})
    ids = [e.id for e in config.endpoints]
    assert ids == ["entsoe-generation-forecast-dayahead",
                   "entsoe-generation-actual-perunit"]
    perunit = config.endpoints[1]
    assert perunit.url == "https://web-api.tp.entsoe.eu/api"
    assert perunit.response_format == "xml"  # native XML — no conversion step
    assert perunit.xslt_path.name == "entsoe-perunit.xslt"
    assert perunit.xslt_path.is_file()
    assert perunit.key_date_offset_days == -1


def _write(tmp_path, doc):
    p = tmp_path / "endpoints.json"
    p.write_text(json.dumps(doc))
    return p


def _endpoint(**extra):
    return {"id": "x", "path": "/api", "responseFormat": "xml",
            "xslt": "tiny-table.xslt", **extra}


TEST_XSLT_DIR = "tests/fixtures/xslt"


def test_duplicate_ids_rejected(tmp_path):
    p = _write(tmp_path, {"baseUrl": "https://x", "endpoints": [_endpoint(), _endpoint()]})
    with pytest.raises(ConfigError, match="duplicate"):
        load_config(p, env={}, xslt_dir=TEST_XSLT_DIR)


def test_missing_stylesheet_named(tmp_path):
    p = _write(tmp_path, {"baseUrl": "https://x",
                          "endpoints": [_endpoint(xslt="ghost.xslt")]})
    with pytest.raises(ConfigError, match="ghost.xslt"):
        load_config(p, env={}, xslt_dir=TEST_XSLT_DIR)


def test_bad_response_format_rejected(tmp_path):
    p = _write(tmp_path, {"baseUrl": "https://x",
                          "endpoints": [_endpoint(responseFormat="yaml")]})
    with pytest.raises(ConfigError, match="yaml"):
        load_config(p, env={}, xslt_dir=TEST_XSLT_DIR)


def test_body_with_get_rejected(tmp_path):
    p = _write(tmp_path, {"baseUrl": "https://x",
                          "endpoints": [_endpoint(body="{}")]})
    with pytest.raises(ConfigError, match="body"):
        load_config(p, env={}, xslt_dir=TEST_XSLT_DIR)


def test_absolute_url_bypasses_base(tmp_path):
    p = _write(tmp_path, {"endpoints": [_endpoint(url="https://other/api", path=None)]})
    config = load_config(p, env={}, xslt_dir=TEST_XSLT_DIR)
    assert config.endpoints[0].url == "https://other/api"


def test_path_without_base_url_rejected(tmp_path):
    p = _write(tmp_path, {"endpoints": [_endpoint()]})
    with pytest.raises(ConfigError, match="baseUrl"):
        load_config(p, env={}, xslt_dir=TEST_XSLT_DIR)


# ------------------------------------------------------------------ http ----

def _config_endpoint(**extra):
    import tempfile
    doc = {"baseUrl": "https://api.test",
           "endpoints": [{**_endpoint(), **extra}]}
    with tempfile.NamedTemporaryFile("w", suffix=".json", delete=False) as f:
        json.dump(doc, f)
    return load_config(f.name, env={}, xslt_dir=TEST_XSLT_DIR).endpoints[0]


def test_build_request_substitutes_everywhere():
    endpoint = _config_endpoint(
        method="POST", body='{"key": "${env:API_KEY}", "day": "${date:%Y-%m-%d:P0D}"}',
        query={"token": "${env:API_KEY}"}, headers={"X-Day": "${date:%Y%m%d:P0D}"},
        timezone="UTC")
    request = build_request(endpoint, {"API_KEY": "sec"}, ANCHOR)
    query = dict(urllib.parse.parse_qsl(urllib.parse.urlparse(request.full_url).query))
    assert query["token"] == "sec"
    assert request.get_header("X-day") == "20260905"
    assert b'"key": "sec"' in request.data
    assert b'"day": "2026-09-05"' in request.data
    assert request.get_method() == "POST"


class FakeResponse:
    def __init__(self, body: bytes):
        self._body = body

    def read(self):
        return self._body

    def __enter__(self):
        return self

    def __exit__(self, *exc):
        return False


def opener_returning(*outcomes):
    calls = []

    def _open(request, timeout=None):
        calls.append(request)
        outcome = outcomes[min(len(calls) - 1, len(outcomes) - 1)]
        if isinstance(outcome, Exception):
            raise outcome
        return FakeResponse(outcome)

    _open.calls = calls
    return _open


@pytest.fixture(autouse=True)
def no_sleep(monkeypatch):
    monkeypatch.setattr(http.time, "sleep", lambda _: None)


def http_error(code: int, body: bytes = b"nope"):
    return urllib.error.HTTPError("url", code, "err", {}, io.BytesIO(body))


def test_fetch_retries_transient_then_succeeds():
    endpoint = _config_endpoint()
    opener = opener_returning(http_error(503), FakeResponse(b"ok")._body)
    raw = fetch(endpoint, {}, ANCHOR, opener=opener)
    assert raw == b"ok"
    assert len(opener.calls) == 2


def test_fetch_client_error_carries_body_for_archiving():
    endpoint = _config_endpoint()
    opener = opener_returning(http_error(400, b"why-it-failed"))
    with pytest.raises(ApiError) as exc_info:
        fetch(endpoint, {}, ANCHOR, opener=opener)
    assert len(opener.calls) == 1
    assert exc_info.value.raw_body == b"why-it-failed"


def test_fetch_network_exhaustion():
    endpoint = _config_endpoint()
    opener = opener_returning(urllib.error.URLError("refused"))
    with pytest.raises(ApiError, match="unreachable after 3"):
        fetch(endpoint, {}, ANCHOR, opener=opener)
    assert len(opener.calls) == 3
