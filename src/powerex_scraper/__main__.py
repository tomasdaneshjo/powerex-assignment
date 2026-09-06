"""Local runner: the identical pipeline, filesystem output.

    python -m powerex_scraper [--endpoint ID] [--date yyyy-mm-dd] [--out DIR]

Secrets come from the environment (${API_KEY} → $API_KEY).
"""
from __future__ import annotations

import argparse
import os
import sys
from datetime import date

from .config import ConfigError, load_config
from .orchestrator import run
from .storage import FileStore


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(prog="python -m powerex_scraper",
                                     description=__doc__.splitlines()[0])
    parser.add_argument("--endpoint", help="one endpoint id (default: all)")
    parser.add_argument("--date", help="anchor date override, yyyy-mm-dd")
    parser.add_argument("--out", default="out", help="output directory (default: ./out)")
    args = parser.parse_args(argv)

    try:
        config = load_config()
        anchor = date.fromisoformat(args.date) if args.date else None
    except (ConfigError, ValueError) as exc:
        print(f"error: {exc}", file=sys.stderr)
        return 2

    summary = run(config, store=FileStore(args.out), env=dict(os.environ),
                  endpoint_ids=[args.endpoint] if args.endpoint else None,
                  anchor_date=anchor)
    for r in summary.results:
        status = "OK  " if r.success else "FAIL"
        detail = f"{r.row_count} rows" if r.success else r.error
        print(f"{status} {r.endpoint_id}: {detail}")
    return 1 if summary.failed else 0


if __name__ == "__main__":
    raise SystemExit(main())
