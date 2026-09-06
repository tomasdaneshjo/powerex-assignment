from datetime import date

import pytest

from powerex_scraper.placeholders import PlaceholderError, resolve_anchor, substitute

ANCHOR = date(2026, 9, 5)
TZ = "Europe/Bratislava"


def test_env_substitution_including_secrets():
    env = {"API_KEY": "sec-123", "REGION": "eu"}
    assert substitute("k=${env:API_KEY}&r=${env:REGION}", env, TZ, ANCHOR) == "k=sec-123&r=eu"


def test_legacy_api_key_shorthand_is_rejected_with_guidance():
    with pytest.raises(PlaceholderError, match="unknown placeholder"):
        substitute("${API_KEY}", {"API_KEY": "x"}, TZ, ANCHOR)


def test_missing_env_var_is_a_named_error():
    with pytest.raises(PlaceholderError, match="NOPE"):
        substitute("${env:NOPE}", {}, TZ, ANCHOR)


def test_date_placeholder_is_local_midnight_in_utc_summer():
    # CET summer: local midnight = 22:00Z of the previous UTC day
    assert substitute("${date:%Y%m%d%H%M:P0D}", {}, TZ, ANCHOR) == "202609042200"
    assert substitute("${date:%Y%m%d%H%M:-P1D}", {}, TZ, ANCHOR) == "202609032200"
    assert substitute("${date:%Y%m%d%H%M:P1D}", {}, TZ, ANCHOR) == "202609052200"


def test_date_placeholder_winter_boundary():
    assert substitute("${date:%Y%m%d%H%M:P0D}", {}, TZ, date(2026, 1, 15)) == "202601142300"


def test_date_placeholder_dst_transition_day_lengths():
    spring = substitute("${date:%Y%m%d%H%M:P0D}", {}, TZ, date(2026, 3, 29))
    next_day = substitute("${date:%Y%m%d%H%M:P1D}", {}, TZ, date(2026, 3, 29))
    assert (spring, next_day) == ("202603282300", "202603292200")  # a 23-hour day


def test_unknown_placeholder_rejected():
    with pytest.raises(PlaceholderError, match="unknown placeholder"):
        substitute("${wat:1}", {}, TZ, ANCHOR)


@pytest.mark.parametrize("bad", ["${date:%Y}", "${date:%Y:1}", "${date:%Y:P}", "${date:%Y:P1X}"])
def test_malformed_date_spec_rejected(bad):
    with pytest.raises(PlaceholderError, match="expected"):
        substitute(bad, {}, TZ, ANCHOR)


def test_duration_time_part_shifts_past_local_midnight():
    assert substitute("${date:%Y%m%d%H%M:PT12H}", {}, TZ, ANCHOR) == "202609051000"


def test_duration_months_are_calendar_correct_with_clamping():
    # Jan 31 + P1M clamps to Feb 28 (2026 is not a leap year); winter = UTC+1
    assert substitute("${date:%Y%m%d%H%M:P1M}", {}, TZ, date(2026, 1, 31)) == "202602272300"


def test_mixed_duration_adds_in_iso_order():
    # -P1M2D: back one calendar month, then two days; addition (unlike
    # scaling) is well-defined for mixed durations
    assert substitute("${date:%Y%m%d:-P1M2D}", {}, TZ, ANCHOR) == "20260802"


def test_resolve_anchor_override_wins():
    assert resolve_anchor(TZ, date(2026, 1, 1)) == date(2026, 1, 1)


def test_date_format_may_contain_colons():
    # ${date:FMT:OFFSET} parses the offset from the RIGHT — ISO-style formats work
    out = substitute("${date:%Y-%m-%dT%H:%M:%SZ:P0D}", {}, TZ, ANCHOR)
    assert out == "2026-09-04T22:00:00Z"
