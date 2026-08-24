#!/usr/bin/env python3
from __future__ import annotations

import argparse
import os
from pathlib import Path

from release_readiness import ReadinessError, build_rc_evidence


def main() -> int:
    parser = argparse.ArgumentParser(description="Build non-delivering v1.1.0-rc1 readiness evidence.")
    parser.add_argument("--root", type=Path, default=Path(__file__).resolve().parent.parent)
    parser.add_argument("--output", type=Path,
                        default=Path("artifacts/release/v1.1.0-rc1-readiness.json"))
    args = parser.parse_args()
    try:
        evidence = build_rc_evidence(args.root, os.environ.get("GITHUB_SHA", ""), args.output)
    except ReadinessError as error:
        print(f"rc-readiness: FAIL: {error}")
        return 1
    print(f"rc-readiness: PASS: {evidence['tag']} at {evidence['commit']}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
