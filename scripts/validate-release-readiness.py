#!/usr/bin/env python3
from __future__ import annotations

import argparse
from pathlib import Path

from release_readiness import ReadinessError, resolve_repository_root, validate_archive_changelog


def main() -> int:
    parser = argparse.ArgumentParser(description="Validate exact OpenSpec archive/CHANGELOG consistency.")
    parser.add_argument("--root", type=Path, default=Path(__file__).resolve().parent.parent)
    args = parser.parse_args()
    try:
        root = resolve_repository_root(args.root)
        archives = validate_archive_changelog(root)
    except ReadinessError as error:
        print(f"release-readiness: FAIL: {error}")
        return 1
    print(f"release-readiness: PASS: {len(archives)} exact archive claims")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
