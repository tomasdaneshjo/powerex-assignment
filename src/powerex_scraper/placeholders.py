"""Placeholder substitution for endpoint declarations.

Supported forms (anywhere in url, query values, header values, body):

    ${env:NAME}                → any environment variable (this is also how
                                 secrets enter — e.g. ${env:API_KEY}; substituted
                                 values are never written to any stored artifact)
    ${date:FORMAT:DURATION}    → local midnight of (anchor date + ISO-8601
                                 DURATION) in the endpoint's timezone, converted
                                 to UTC and rendered with the strftime FORMAT
                                 e.g. ${date:%Y%m%d%H%M:-P1D} → yesterday 22:00Z
                                 in summer for a CET endpoint

Duration components apply in ISO order: years/months as calendar arithmetic on
the anchor date (with end-of-month clamping), weeks/days as day arithmetic,
then any time part shifts the local wall clock past midnight. A leading '-'
negates the whole duration. Adding a mixed duration such as P1M1D is
well-defined (unlike *scaling* one, which the XSLT layer rightly refuses).

The anchor date defaults to "today" in the endpoint's timezone and can be
overridden per run (backfills reuse the same code path).
"""
from __future__ import annotations

import re
from datetime import date, datetime, time, timezone
from zoneinfo import ZoneInfo

import isodate

_PLACEHOLDER = re.compile(r"\$\{([^}]+)\}")


class PlaceholderError(ValueError):
    """A placeholder is unknown or its inputs are missing — named message."""


def resolve_anchor(tz_name: str, anchor: date | None = None,
                   now: datetime | None = None) -> date:
    if anchor is not None:
        return anchor
    now = now or datetime.now(timezone.utc)
    return now.astimezone(ZoneInfo(tz_name)).date()


def _date_value(fmt: str, duration: str, tz_name: str, anchor: date) -> str:
    # isodate.ISO8601Error is a ValueError; the caller maps it to a named
    # PlaceholderError. Aware-datetime arithmetic in Python is wall-clock, so
    # local midnight + duration behaves like the ISO calendar addition
    # (year/month components clamp to month end via isodate.Duration).
    offset = isodate.parse_duration(duration)
    local = datetime.combine(anchor, time.min, ZoneInfo(tz_name)) + offset
    return local.astimezone(timezone.utc).strftime(fmt)


def substitute(text: str, env: dict[str, str], tz_name: str, anchor: date) -> str:
    def replace(match: re.Match) -> str:
        token = match.group(1)
        if token.startswith("env:"):
            name = token[4:]
            value = env.get(name)
            if value is None:
                raise PlaceholderError(f"${{env:{name}}}: environment variable is not set")
            return value
        if token.startswith("date:"):
            try:
                fmt, duration = token[5:].rsplit(":", 1)  # format may contain colons
                return _date_value(fmt, duration, tz_name, anchor)
            except ValueError as exc:
                raise PlaceholderError(
                    f"${{{token}}}: expected ${{date:FORMAT:ISO_DURATION}}, "
                    f"e.g. ${{date:%Y%m%d%H%M:-P1D}}") from exc
        raise PlaceholderError(f"${{{token}}}: unknown placeholder")

    return _PLACEHOLDER.sub(replace, text)
