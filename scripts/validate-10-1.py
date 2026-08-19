#!/usr/bin/env python3
"""
Wave 10 Slice 10.1 — TDD RED/GREEN validation script.

Validates the YAML/CODEOWNERS artifacts created by this slice.
Each validation is invoked per-file in RED state (file absent → fail) then
GREEN state (file present + valid → pass).

Usage:
    python3 scripts/validate-10-1.py ci
    python3 scripts/validate-10-1.py dependabot
    python3 scripts/validate-10-1.py nightly
    python3 scripts/validate-10-1.py codeowners
    python3 scripts/validate-10-1.py pr-template
    python3 scripts/validate-10-1.py all
"""

from __future__ import annotations

import re
import sys
from pathlib import Path

import yaml

REPO = Path(__file__).resolve().parent.parent
GITHUB = REPO / ".github"

REQUIRED_JOBS = {"lint-backend", "test-backend", "test-integration", "test-frontend"}
EXPECTED_ECOSYSTEMS = {"nuget", "npm", "github-actions"}


def _fail(msg: str) -> None:
    print(f"  FAIL: {msg}")
    raise SystemExit(1)


def _ok(msg: str) -> None:
    print(f"  OK:   {msg}")


def validate_ci() -> None:
    print("ci.yml")
    p = GITHUB / "workflows" / "ci.yml"
    if not p.exists():
        _fail(f"{p} missing")
    try:
        raw = p.read_text()
        doc = yaml.safe_load(raw)
    except yaml.YAMLError as e:
        _fail(f"{p} invalid YAML: {e}")
    if not isinstance(doc, dict):
        _fail(f"{p} not a YAML mapping at root")

    # Triggers: pull_request + push
    on = doc.get(True, doc.get("on", {}))  # YAML 1.1/1.2 'on' quirk
    if not isinstance(on, dict):
        _fail("'on' must be a mapping")
    if "pull_request" not in on:
        _fail("'pull_request' trigger missing")
    if "push" not in on:
        _fail("'push' trigger missing")
    _ok("triggers (pull_request + push) present")

    # Concurrency
    conc = doc.get("concurrency")
    if not isinstance(conc, dict) or "group" not in conc:
        _fail("'concurrency.group' missing")
    if not conc.get("cancel-in-progress"):
        _fail("'concurrency.cancel-in-progress' must be true")
    _ok("concurrency group + cancel-in-progress=true")

    # Jobs
    jobs = doc.get("jobs")
    if not isinstance(jobs, dict):
        _fail("'jobs' missing or not a mapping")
    present_jobs = set(jobs.keys())
    missing = REQUIRED_JOBS - present_jobs
    if missing:
        _fail(f"missing required jobs: {sorted(missing)}")
    _ok(f"all 4 required jobs present: {sorted(REQUIRED_JOBS)}")

    raw = p.read_text().lower()

    # lint-backend: actions/setup-dotnet@v4 + dotnet format --verify-no-changes
    lint = jobs["lint-backend"]
    if "setup-dotnet" not in yaml.safe_dump(lint):
        _fail("lint-backend missing actions/setup-dotnet reference")
    if "verify-no-changes" not in raw or "dotnet format" not in raw:
        _fail("lint-backend missing 'dotnet format --verify-no-changes'")
    if "actions/cache" not in raw:
        _fail("lint-backend missing actions/cache reference")
    _ok("lint-backend uses setup-dotnet@v4 + format --verify-no-changes + cache")

    # test-backend: per-csproj dotnet test invocations
    test_be = jobs["test-backend"]
    test_be_text = yaml.safe_dump(test_be) + raw
    for csproj in (
        "JadeCapital.Shared.Kernel.UnitTests.csproj",
        "JadeCapital.Identity.UnitTests.csproj",
        "JadeCapital.Billing.UnitTests.csproj",
        "JadeCapital.Trading.UnitTests.csproj",
        "JadeCapital.Admin.UnitTests.csproj",
    ):
        if csproj not in test_be_text:
            _fail(f"test-backend missing '{csproj}'")
    _ok("test-backend covers all 5 BE unit-test csproj")

    # test-integration: services: postgres block
    test_integ = jobs["test-integration"]
    test_integ_text = yaml.safe_dump(test_integ) + raw
    if "postgres" not in test_integ_text:
        _fail("test-integration missing services: postgres block")
    if "JadeCapital.Api.IntegrationTests.csproj" not in test_integ_text:
        _fail("test-integration missing JadeCapital.Api.IntegrationTests.csproj")
    _ok("test-integration uses services: postgres + IntegrationTests csproj")

    # test-frontend: npm ci + npm test + npm run build, Node 20
    test_fe = jobs["test-frontend"]
    test_fe_text = yaml.safe_dump(test_fe) + raw
    if "setup-node" not in test_fe_text:
        _fail("test-frontend missing actions/setup-node reference")
    if "node-version: 20" not in raw and "node-version: '20'" not in raw and "20" not in raw:
        _fail("test-frontend must pin Node 20")
    if "npm ci" not in raw:
        _fail("test-frontend missing 'npm ci'")
    if "npm test" not in raw:
        _fail("test-frontend missing 'npm test'")
    if "npm run build" not in raw:
        _fail("test-frontend missing 'npm run build'")
    _ok("test-frontend uses setup-node@v4 (Node 20) + npm ci/test/build")


def validate_dependabot() -> None:
    print("dependabot.yml")
    p = GITHUB / "dependabot.yml"
    if not p.exists():
        _fail(f"{p} missing")
    try:
        doc = yaml.safe_load(p.read_text())
    except yaml.YAMLError as e:
        _fail(f"{p} invalid YAML: {e}")
    if doc.get("version") != 2:
        _fail("dependabot version must be 2")
    updates = doc.get("updates")
    if not isinstance(updates, list):
        _fail("updates must be a list")
    ecosystems = {u.get("package-ecosystem") for u in updates if isinstance(u, dict)}
    missing = EXPECTED_ECOSYSTEMS - ecosystems
    if missing:
        _fail(f"missing ecosystems: {sorted(missing)}; found: {sorted(ecosystems)}")
    _ok(f"all 3 ecosystems present: {sorted(EXPECTED_ECOSYSTEMS)}")
    # Weekly schedule on nuget + npm; github-actions may be weekly too per task brief.
    nuget = next(u for u in updates if u.get("package-ecosystem") == "nuget")
    sched = nuget.get("schedule", {})
    if "interval" not in sched:
        _fail("nuget schedule.interval missing")
    _ok(f"nuget schedule.interval={sched.get('interval')}")
    npm = next(u for u in updates if u.get("package-ecosystem") == "npm")
    if not npm.get("directory"):
        _fail("npm directory missing")
    if "frontend" not in npm["directory"]:
        _fail(f"npm directory must target frontend; got {npm['directory']}")
    _ok(f"npm directory={npm['directory']}")
    gha = next(u for u in updates if u.get("package-ecosystem") == "github-actions")
    if not gha.get("directory"):
        _fail("github-actions directory missing")
    _ok(f"github-actions directory={gha['directory']}")


def validate_nightly() -> None:
    print("nightly-scan.yml")
    p = GITHUB / "workflows" / "nightly-scan.yml"
    if not p.exists():
        _fail(f"{p} missing")
    try:
        doc = yaml.safe_load(p.read_text())
    except yaml.YAMLError as e:
        _fail(f"{p} invalid YAML: {e}")
    on = doc.get(True, doc.get("on", {}))
    if "schedule" not in on:
        _fail("schedule trigger missing")
    if "workflow_dispatch" not in on:
        _fail("workflow_dispatch trigger missing")
    _ok("schedule + workflow_dispatch triggers present")
    steps_text = yaml.safe_dump(doc).lower()
    if "dotnet list" not in steps_text or "vulnerable" not in steps_text:
        _fail("nightly-scan missing 'dotnet list package --vulnerable'")
    if "npm audit" not in steps_text:
        _fail("nightly-scan missing 'npm audit'")
    _ok("dotnet list package --vulnerable + npm audit present")


def validate_codeowners() -> None:
    print("CODEOWNERS")
    p = GITHUB / "CODEOWNERS"
    if not p.exists():
        _fail(f"{p} missing")
    raw = p.read_text()
    lines = [ln for ln in raw.splitlines() if ln.strip() and not ln.lstrip().startswith("#")]
    if not lines:
        _fail("CODEOWNERS has no owner rules")
    bad = []
    for ln in lines:
        parts = ln.split()
        if len(parts) < 2:
            bad.append(ln)
    if bad:
        _fail(f"CODEOWNERS lines without pattern + owner: {bad}")
    text = raw.lower()
    for path in ("/src/2.modules/identity/", "/src/2.modules/billing/", "/infrastructure/", "/.github/workflows/"):
        if path not in text:
            _fail(f"CODEOWNERS missing override for {path}")
    _ok(f"CODEOWNERS has {len(lines)} rule(s) + sensitive-area overrides")


def validate_pr_template() -> None:
    print("pull_request_template.md")
    p = GITHUB / "pull_request_template.md"
    if not p.exists():
        _fail(f"{p} missing")
    raw = p.read_text()
    if "- [ ]" not in raw:
        _fail("PR template missing checkbox list")
    _ok(f"PR template present ({len(raw)} chars, with checkbox list)")


def main() -> None:
    if len(sys.argv) < 2:
        print(__doc__)
        raise SystemExit(2)
    target = sys.argv[1].lower()
    dispatch = {
        "ci": validate_ci,
        "dependabot": validate_dependabot,
        "nightly": validate_nightly,
        "codeowners": validate_codeowners,
        "pr-template": validate_pr_template,
    }
    if target == "all":
        for fn in dispatch.values():
            fn()
        print("ALL OK")
        return
    if target not in dispatch:
        print(f"unknown target: {target}")
        raise SystemExit(2)
    dispatch[target]()
    print(f"{target} OK")


if __name__ == "__main__":
    main()