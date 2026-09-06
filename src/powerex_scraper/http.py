"""Request building and execution for declared endpoints.

Placeholders are substituted at request time (url, query values, header
values, body). Stdlib urllib with bounded retries: transient statuses and
network errors back off exponentially with jitter; other failures raise
ApiError with a diagnosable excerpt and the raw body (when one was received)
so the caller can archive it.
"""
from __future__ import annotations

import random
import time
import urllib.error
import urllib.parse
import urllib.request
from datetime import date

from .config import Endpoint
from .placeholders import substitute

RETRYABLE_STATUS = {408, 429, 500, 502, 503, 504}
MAX_ATTEMPTS = 3
TIMEOUT_SECONDS = 30


class ApiError(RuntimeError):
    def __init__(self, message: str, raw_body: bytes | None = None):
        super().__init__(message)
        self.raw_body = raw_body


def build_request(endpoint: Endpoint, env: dict[str, str],
                  anchor: date) -> urllib.request.Request:
    def sub(text: str) -> str:
        return substitute(text, env, endpoint.timezone, anchor)

    url = sub(endpoint.url)
    if endpoint.query:
        query = urllib.parse.urlencode({k: sub(v) for k, v in endpoint.query.items()})
        url = f"{url}{'&' if '?' in url else '?'}{query}"
    headers = {k: sub(v) for k, v in endpoint.headers.items()}
    headers.setdefault("User-Agent", "universal-scraper/1.0")
    body = sub(endpoint.body).encode("utf-8") if endpoint.body is not None else None
    return urllib.request.Request(url, data=body, headers=headers, method=endpoint.method)


def fetch(endpoint: Endpoint, env: dict[str, str], anchor: date,
          opener=None) -> bytes:
    request = build_request(endpoint, env, anchor)
    open_fn = opener or urllib.request.urlopen

    for attempt in range(1, MAX_ATTEMPTS + 1):
        try:
            with open_fn(request, timeout=TIMEOUT_SECONDS) as response:
                return response.read()
        except urllib.error.HTTPError as exc:
            body = exc.read()
            if exc.code in RETRYABLE_STATUS and attempt < MAX_ATTEMPTS:
                pass  # fall through to backoff
            else:
                suffix = f" on all {MAX_ATTEMPTS} attempts" \
                    if exc.code in RETRYABLE_STATUS else ""
                raise ApiError(
                    f"HTTP {exc.code}{suffix}: {body[:300].decode('utf-8', 'replace')}",
                    raw_body=body) from exc
        except (urllib.error.URLError, TimeoutError, OSError) as exc:
            if attempt == MAX_ATTEMPTS:
                raise ApiError(f"unreachable after {MAX_ATTEMPTS} attempts: {exc}") from exc
        time.sleep((2 ** attempt) + random.uniform(0, 1))
    raise ApiError("retries exhausted")  # pragma: no cover
