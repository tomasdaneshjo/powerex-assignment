"""AWS Lambda entry point.

Event (EventBridge Scheduler passes it verbatim):
    {"endpointIds": ["..."], "anchorDate": "yyyy-mm-dd"}
Both optional: no ids = all endpoints; anchorDate replaces "today" so a manual
invoke backfills any day. Partial failure stores every successful endpoint's
artifacts, then raises so the invocation lands in the Lambda Errors metric.
"""
from __future__ import annotations

import os
from datetime import date

from .config import ConfigError, load_config
from .orchestrator import ScrapeFailedError, run
from .storage import S3Store


def lambda_handler(event, context):
    event = event or {}
    config = load_config()
    if not config.bucket:
        raise ConfigError("OUTPUT_BUCKET environment variable is required")

    anchor = event.get("anchorDate")
    if anchor:
        try:
            date.fromisoformat(anchor)
        except ValueError as exc:
            raise ConfigError(f"anchorDate '{anchor}' is not yyyy-mm-dd") from exc
    summary = run(
        config,
        store=S3Store(config.bucket),
        env=dict(os.environ),
        endpoint_ids=event.get("endpointIds"),
        anchor_date=date.fromisoformat(anchor) if anchor else None,
    )
    if summary.failed:
        raise ScrapeFailedError(summary)
    return summary.to_dict()
