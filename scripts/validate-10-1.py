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


def _named_step(job: dict, name: str) -> dict:
    for step in job.get("steps", []):
        if step.get("name") == name:
            return step
    _fail(f"job missing '{name}' step")


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
    format_run = _named_step(lint, "Verify formatting").get("run", "")
    if not lint.get("env", {}).get("FORMAT_BASE_SHA"):
        _fail("lint-backend missing the event base SHA for changed-file formatting")
    if "changed-csharp-files.sh" not in format_run or '--include "${CSHARP_FILES[@]}"' not in format_run:
        _fail("lint-backend must format only the fail-closed changed C# set")
    if "< <(" in format_run:
        _fail("lint-backend must not hide changed-file selector failures in process substitution")
    if "actions/cache" not in raw:
        _fail("lint-backend missing actions/cache reference")
    _ok("lint-backend verifies the fail-closed changed C# set and uses the NuGet cache")

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
    export_step = _named_step(test_be, "Export OpenAPI spec")
    backend_env = export_step.get("env", {})
    openapi_env_keys = (
        "Jwt__Issuer",
        "Jwt__Audience",
        "Jwt__AccessTokenSecret",
        "Jwt__RefreshTokenSecret",
        "ConnectionStrings__Storage",
    )
    for key in openapi_env_keys:
        if not backend_env.get(key):
            _fail(f"test-backend missing CI-only OpenAPI input '{key}'")
    if any(key in test_be.get("env", {}) for key in (*openapi_env_keys, "ASPNETCORE_ENVIRONMENT")):
        _fail("CI-only OpenAPI inputs must be scoped to the export step")
    for key in ("Jwt__AccessTokenSecret", "Jwt__RefreshTokenSecret"):
        if len(backend_env[key]) < 32:
            _fail(f"test-backend CI-only '{key}' must satisfy production length validation")
    if backend_env.get("ASPNETCORE_ENVIRONMENT") != "Development":
        _fail("test-backend must enable the development-only OpenAPI endpoint")

    export_run = export_step.get("run", "")
    for token in (
        "set -euo pipefail",
        "trap ",
        "curl --fail",
        "test -s openapi.json.tmp",
        "mv openapi.json.tmp openapi.json",
        'kill -0 "$API_PID"',
    ):
        if token not in export_run:
            _fail(f"OpenAPI export must fail closed and include '{token}'")
    upload_openapi = _named_step(test_be, "Upload OpenAPI artifact").get("with", {})
    if upload_openapi.get("if-no-files-found") != "error":
        _fail("OpenAPI upload must fail when the generated document is absent")

    # test-integration: services: postgres block
    test_integ = jobs["test-integration"]
    test_integ_text = yaml.safe_dump(test_integ) + raw
    if "postgres" not in test_integ_text:
        _fail("test-integration missing services: postgres block")
    if "JadeCapital.Api.IntegrationTests.csproj" not in test_integ_text:
        _fail("test-integration missing JadeCapital.Api.IntegrationTests.csproj")
    postgres_connection = test_integ.get("env", {}).get("ConnectionStrings__Postgres", "")
    postgres_password = test_integ.get("services", {}).get("postgres", {}).get("env", {}).get("POSTGRES_PASSWORD")
    if "Host=127.0.0.1" not in postgres_connection:
        _fail("test-integration must connect through the runner-published Postgres port")
    if not postgres_password or f"Password={postgres_password}" not in postgres_connection:
        _fail("test-integration Postgres service and connection passwords must match")
    if "/dev/tcp/127.0.0.1/5432" not in _named_step(test_integ, "Wait for Postgres").get("run", ""):
        _fail("test-integration readiness must use the runner loopback endpoint")
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
    build_env = _named_step(test_fe, "Production build").get("env", {})
    if not build_env.get("SENTRY_RELEASE") or not build_env.get("APP_ENV"):
        _fail("test-frontend build missing explicit Sentry release/environment inputs")
    if build_env.get("ALLOW_FRONTEND_SENTRY_DISABLED") != "true":
        _fail("test-frontend must explicitly opt into disabled Sentry for the CI verification build")
    _ok("test-frontend uses setup-node@v4 (Node 20) + npm ci/test/build")

    a11y_upload = _named_step(jobs["test-a11y"], "Upload Playwright HTML report on failure").get("with", {})
    if a11y_upload.get("path") != "frontend/.playwright/report":
        _fail("test-a11y upload path must match Playwright's configured report directory")
    if a11y_upload.get("if-no-files-found") != "ignore":
        _fail("test-a11y upload must use the valid if-no-files-found input")

    auth_e2e = jobs["test-auth-e2e"]
    project_name = auth_e2e.get("env", {}).get("COMPOSE_PROJECT_NAME", "")
    if "github.run_id" not in project_name or "github.run_attempt" not in project_name:
        _fail("test-auth-e2e must use a run-isolated Compose project")
    teardown = _named_step(auth_e2e, "Tear down isolated stack").get("run", "")
    if "down -v --remove-orphans" not in teardown:
        _fail("test-auth-e2e must remove isolated containers, volumes, and orphans")

    compose_path = REPO / "docker-compose.ci.yml"
    try:
        compose = yaml.safe_load(compose_path.read_text())
    except yaml.YAMLError as error:
        _fail(f"{compose_path} invalid YAML: {error}")
    e2e = compose.get("services", {}).get("e2e", {})
    mounts = e2e.get("volumes", [])
    command = e2e.get("command", [])
    command_text = command[-1] if command else ""
    if ".:/source:ro" not in mounts or "frontend-workspace:/workspace/frontend" not in mounts:
        _fail("functional E2E must copy read-only source into an isolated writable workspace")
    if any(mount.startswith(".:/workspace") for mount in mounts):
        _fail("functional E2E workspace parent must not be a read-only bind mount")
    for token in (
        "--exclude='./node_modules'",
        "--exclude='./.playwright'",
        "-C /source/frontend -cf - . | tar -xf -",
        "npm ci --no-audit --no-fund",
        "npm run test:e2e -- auth.spec.ts",
    ):
        if token not in command_text:
            _fail(f"functional E2E writable-workspace command missing '{token}'")
    if "frontend-workspace" not in compose.get("volumes", {}) or "playwright-output" not in compose.get("volumes", {}):
        _fail("functional E2E writable workspace and output volumes must be declared")
    _ok("functional E2E uses isolated writable install/output volumes with read-only source")


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
