#!/usr/bin/env python3
"""
Wave 10 Slice 10.2 - TDD RED/GREEN validation script.

Validates the prod-deployment artifacts created by this slice:
- docker-compose.prod.yml (canonical via `docker compose ... config`)
- infrastructure/Dockerfile.api.prod (structural)
- infrastructure/Dockerfile.frontend.prod (structural)
- src/1.Api/JadeCapital.Host/Configuration/DockerSecretConfigurationProvider.cs (structural)
- src/1.Api/JadeCapital.Host/Program.cs wiring (structural)

Each validation is invoked per-artifact in RED state (file absent -> fail)
then GREEN state (file present + valid -> pass).

Usage:
    python3 scripts/validate-10-2.py compose
    python3 scripts/validate-10-2.py dockerfile-api
    python3 scripts/validate-10-2.py dockerfile-frontend
    python3 scripts/validate-10-2.py secrets-provider
    python3 scripts/validate-10-2.py program-wiring
    python3 scripts/validate-10-2.py all
"""
from __future__ import annotations

import re
import shutil
import subprocess
import sys
from pathlib import Path

REPO = Path(__file__).resolve().parent.parent
INFRA = REPO / "infrastructure"
HOST = REPO / "src" / "1.Api" / "JadeCapital.Host"
CONFIGURATION = HOST / "Configuration"
PROGRAM_CS = HOST / "Program.cs"


def _fail(msg: str) -> None:
    print(f"  FAIL: {msg}")
    raise SystemExit(1)


def _ok(msg: str) -> None:
    print(f"  OK:   {msg}")


def _docker_available() -> bool:
    return shutil.which("docker") is not None


def _validate_with_docker_compose(path: Path) -> None:
    """Run `docker compose -f <path> config`; exit code != 0 fails.

    Use a temporary directory so /run/secrets lookups during the compose
    schema validator do not blow up; we do not actually start anything.
    """
    if not path.exists():
        _fail(f"{path} missing")
    if not _docker_available():
        # Degrade gracefully when Docker is missing on the test machine
        # (the script is still useful as a structural validator).
        _ok(f"{path} present (docker not available - skipping compose validator)")
        return
    try:
        result = subprocess.run(
            ["docker", "compose", "-f", str(path), "config", "--quiet"],
            capture_output=True,
            text=True,
            check=False,
            timeout=60,
        )
    except subprocess.TimeoutExpired:
        _fail(f"{path}: docker compose config timed out after 60s")
    if result.returncode != 0:
        out = (result.stdout or "") + (result.stderr or "")
        _fail(f"{path}: docker compose config exited {result.returncode}\n{out.strip()}")
    _ok(f"{path} valid (docker compose config --quiet exit 0)")


# -----------------------------
# Phase 1: docker-compose.prod.yml
# -----------------------------


def validate_compose() -> None:
    print("compose")
    path = REPO / "docker-compose.prod.yml"
    if not path.exists():
        _fail(f"{path} missing")
    raw = path.read_text()

    # Required services
    required_services = {
        "postgres": ["image", "restart", "healthcheck", "expose"],
        "redis": ["image", "restart", "healthcheck", "expose"],
        "minio": ["image", "restart", "healthcheck", "expose"],
        "api": ["build", "restart", "healthcheck", "expose", "secrets"],
        "frontend": ["build", "restart", "healthcheck", "expose"],
        "nginx": ["image", "restart", "ports"],
    }
    for service, required_keys in required_services.items():
        m = re.search(rf"^\s{{2}}{service}:\s*$", raw, re.MULTILINE)
        if not m:
            _fail(f"service `{service}` missing")
        block = _slice_block(raw, m.start())
        for key in required_keys:
            if not re.search(rf"^\s{{4}}{key}:", block, re.MULTILINE):
                _fail(f"service `{service}` missing `{key}:`")

    # mailpit MUST NOT be in prod (it's a dev-only SMTP capture)
    if re.search(r"^\s{2}mailpit:\s*$", raw, re.MULTILINE):
        _fail("`mailpit` service must NOT appear in docker-compose.prod.yml")

    # Postgres + Redis MUST use expose only (no ports:)
    for service in ("postgres", "redis"):
        m = re.search(rf"^\s{{2}}{service}:\s*$", raw, re.MULTILINE)
        if not m:
            continue
        block = _slice_block(raw, m.start())
        if re.search(r"^\s{4}ports:", block, re.MULTILINE):
            _fail(f"service `{service}` must not declare `ports:` (use `expose:` only)")

    # Secrets block must declare all expected secret names
    expected_secrets = {
        "postgres_password",
        "minio_root_user",
        "minio_root_password",
        "jwt_access_token_secret",
        "jwt_refresh_token_secret",
        "mailgun_api_key",
        "stripe_api_key",
        "stripe_webhook_secret",
    }
    secrets_match = re.search(r"^secrets:\s*$", raw, re.MULTILINE)
    if not secrets_match:
        _fail("top-level `secrets:` block missing")
    secrets_block = _slice_block(raw, secrets_match.start())
    for name in expected_secrets:
        if not re.search(rf"^\s{{2}}{name}:\s*$", secrets_block, re.MULTILINE):
            _fail(f"secret `{name}` missing from top-level `secrets:` block")
        sub = _slice_block(secrets_block, secrets_block.index(name))
        if not re.search(rf"^\s{{4}}file:\s+\./infrastructure/secrets/{name}\.txt", sub, re.MULTILINE):
            _fail(f"secret `{name}` must be backed by ./infrastructure/secrets/{name}.txt")

    # No environment-level secrets (everything must be file-mounted)
    banned_env_keys = (
        "POSTGRES_PASSWORD:",
        "MINIO_ROOT_USER:",
        "MINIO_ROOT_PASSWORD:",
        "JWT__AccessTokenSecret:",
        "JWT__RefreshTokenSecret:",
        "STRIPE_SECRET_KEY:",
        "STRIPE_WEBHOOK_SECRET:",
    )
    for banned in banned_env_keys:
        if re.search(rf"^\s{{6}}{re.escape(banned)}", raw, re.MULTILINE):
            _fail(
                f"prod compose must NOT inline plaintext secret `{banned}` "
                f"(use `<NAME>__File:` pointing at /run/secrets/<name>)"
            )

    # Canonical schema pass via docker compose (the gold standard)
    _validate_with_docker_compose(path)


def _slice_block(text: str, start: int) -> str:
    """Return the YAML block starting at `start` until the next same-indent
    sibling or end-of-text. Used to scope structural checks to a single
    service / secret definition without a full YAML parse."""
    lines = text.splitlines(keepends=True)
    # find the line where `start` lands
    cum = 0
    start_line = 0
    for i, line in enumerate(lines):
        cum += len(line)
        if cum > start:
            start_line = i
            break
    base_indent = len(lines[start_line]) - len(lines[start_line].lstrip())
    out: list[str] = []
    for line in lines[start_line + 1 :]:
        stripped = line.rstrip("\n")
        if not stripped.strip():
            out.append(line)
            continue
        indent = len(stripped) - len(stripped.lstrip())
        # any sibling at the original indent terminates the block
        if indent <= base_indent and stripped.strip():
            break
        out.append(line)
    return "".join(out)


# -----------------------------
# Phase 2: Dockerfile.api.prod
# -----------------------------


def validate_dockerfile_api() -> None:
    print("dockerfile-api")
    path = INFRA / "Dockerfile.api.prod"
    if not path.exists():
        _fail(f"{path} missing")
    raw = path.read_text()
    _require_dockerfile_basics(path, raw)
    # Must run as non-root user named `jade`
    if not re.search(r"^USER\s+jade\s*$", raw, re.MULTILINE):
        _fail(f"{path} missing `USER jade` directive (non-root requirement)")
    if "EXPOSE 8080" not in raw:
        _fail(f"{path} must declare `EXPOSE 8080`")
    if not re.search(r"^FROM\s+mcr\.microsoft\.com/dotnet/sdk:10\.0\s+AS\s+build", raw, re.MULTILINE):
        _fail(f"{path} must use `mcr.microsoft.com/dotnet/sdk:10.0 AS build` for the build stage")
    if not re.search(r"^FROM\s+mcr\.microsoft\.com/dotnet/aspnet:10\.0", raw, re.MULTILINE):
        _fail(f"{path} must use `mcr.microsoft.com/dotnet/aspnet:10.0` runtime base")
    if "dotnet publish" not in raw:
        _fail(f"{path} must use `dotnet publish` (not raw `dotnet build`)")
    # HEALTHCHECK directive
    if "HEALTHCHECK" not in raw:
        _fail(f"{path} must declare a HEALTHCHECK directive")
    # /health/live endpoint is the healthcheck target
    if "/health/live" not in raw:
        _fail(f"{path} HEALTHCHECK must probe /health/live")
    # Restore layer must copy csproj BEFORE the bulk COPY . .
    restore_lines = [ln for ln in raw.splitlines() if "dotnet restore" in ln]
    if not restore_lines:
        _fail(f"{path} must invoke `dotnet restore` for the layered NuGet cache")
    bulk_copy = re.search(r"^COPY\s+\.\s+\.\s*$", raw, re.MULTILINE)
    restore_line_idx = raw.find("dotnet restore")
    if bulk_copy and bulk_copy.start() < restore_line_idx:
        _fail(f"{path} COPY . . must come AFTER `dotnet restore` (cache discipline)")
    _ok(f"{path} structurally valid (multi-stage, non-root, HEALTHCHECK, EXPOSE 8080)")


# -----------------------------
# Phase 3: Dockerfile.frontend.prod
# -----------------------------


def validate_dockerfile_frontend() -> None:
    print("dockerfile-frontend")
    path = INFRA / "Dockerfile.frontend.prod"
    if not path.exists():
        _fail(f"{path} missing")
    raw = path.read_text()
    _require_dockerfile_basics(path, raw)
    if not re.search(r"^FROM\s+node:20-alpine\s+AS\s+build", raw, re.MULTILINE):
        _fail(f"{path} must use `node:20-alpine AS build` for the build stage")
    if not re.search(r"^FROM\s+nginx:1\.27-alpine", raw, re.MULTILINE):
        _fail(f"{path} must use `nginx:1.27-alpine` runtime base")
    if "npm ci" not in raw:
        _fail(f"{path} must use `npm ci` (deterministic install)")
    if "EXPOSE 80" not in raw:
        _fail(f"{path} must declare `EXPOSE 80`")
    # HEALTHCHECK directive (nginx:1.27-alpine has wget/curl available)
    if "HEALTHCHECK" not in raw:
        _fail(f"{path} must declare a HEALTHCHECK directive")
    _ok(f"{path} structurally valid (multi-stage node build -> nginx runtime)")


def _require_dockerfile_basics(path: Path, raw: str) -> None:
    if not raw.strip():
        _fail(f"{path} empty")
    # Must have at least one FROM
    if not re.search(r"^FROM\s+\S+", raw, re.MULTILINE):
        _fail(f"{path} missing FROM instruction")
    # Must have an ENTRYPOINT or CMD
    if not (re.search(r"^ENTRYPOINT\s+", raw, re.MULTILINE) or re.search(r"^CMD\s+", raw, re.MULTILINE)):
        _fail(f"{path} missing ENTRYPOINT or CMD instruction")


# -----------------------------
# Phase 4: DockerSecretConfigurationProvider
# -----------------------------


def validate_secrets_provider() -> None:
    print("secrets-provider")
    path = CONFIGURATION / "DockerSecretConfigurationProvider.cs"
    if not path.exists():
        _fail(f"{path} missing")
    raw = path.read_text()
    # Class declaration with both Provider + Source
    if not re.search(r"class\s+DockerSecretConfigurationProvider\s*:\s*ConfigurationProvider", raw):
        _fail(f"{path} must declare `class DockerSecretConfigurationProvider : ConfigurationProvider`")
    if not re.search(r"class\s+DockerSecretConfigurationSource\s*:\s*IConfigurationSource", raw):
        _fail(f"{path} must declare `class DockerSecretConfigurationSource : IConfigurationSource`")
    # Constants
    if '"/run/secrets"' not in raw:
        _fail(f"{path} must define `/run/secrets` directory constant")
    # Public Load override
    if not re.search(r"public\s+override\s+void\s+Load\s*\(", raw):
        _fail(f"{path} must override `public override void Load()`")
    # Silently skip when /run/secrets absent (defensive)
    if "Directory.Exists" not in raw:
        _fail(f"{path} must call `Directory.Exists(...)` to gracefully skip when secrets absent")
    # Trim whitespace
    if ".Trim()" not in raw:
        _fail(f"{path} must `.Trim()` secret values (Docker appends newlines)")
    # ConfigPrefix discriminator in the data key
    if not re.search(r"__Secret:", raw):
        _fail(f"{path} must prefix secrets with `__Secret:` so they are distinct from env vars")
    _ok(f"{path} structurally valid (Provider + Source + Load override + safe defaults)")


# -----------------------------
# Phase 5: Program.cs wiring
# -----------------------------


def validate_program_wiring() -> None:
    print("program-wiring")
    if not PROGRAM_CS.exists():
        _fail(f"{PROGRAM_CS} missing")
    raw = PROGRAM_CS.read_text()
    # Using directive for the new namespace
    if "JadeCapital.Host.Configuration" not in raw:
        _fail(f"{PROGRAM_CS} must `using JadeCapital.Host.Configuration;`")
    # Must call Add() with the new source — accept either direct or via IConfigurationBuilder cast
    patterns = (
        r"\(?\(IConfigurationBuilder\)?\s*\.?\s*builder\.Configuration\)?\.Add\(new\s+DockerSecretConfigurationSource\(\)\)",
        r"builder\.Configuration\.Add\(new\s+DockerSecretConfigurationSource\(\)\)",
    )
    if not any(re.search(p, raw) for p in patterns):
        _fail(
            f"{PROGRAM_CS} must call `builder.Configuration.Add(new DockerSecretConfigurationSource())` "
            "(or `((IConfigurationBuilder)builder.Configuration).Add(...)` if disambiguating)"
        )
    # Must be BEFORE Configure<JwtOptions>(...)
    add_idx = raw.find("DockerSecretConfigurationSource()")
    configure_idx = raw.find("builder.Services.Configure<JwtOptions>")
    if add_idx < 0 or configure_idx < 0:
        _fail(f"{PROGRAM_CS} cannot locate Add(...) and/or Configure<JwtOptions>(...)")
    if add_idx > configure_idx:
        _fail(f"{PROGRAM_CS} Add(DockerSecretConfigurationSource) MUST come BEFORE Configure<JwtOptions>(...)")
    _ok(f"{PROGRAM_CS} wires DockerSecretConfigurationSource BEFORE Configure<JwtOptions>")


# -----------------------------
# Orchestration
# -----------------------------


CHECKS = {
    "compose": validate_compose,
    "dockerfile-api": validate_dockerfile_api,
    "dockerfile-frontend": validate_dockerfile_frontend,
    "secrets-provider": validate_secrets_provider,
    "program-wiring": validate_program_wiring,
}


def main() -> int:
    args = sys.argv[1:]
    if not args or args[0] == "all":
        targets = list(CHECKS)
    else:
        targets = args
    failed = 0
    for name in targets:
        fn = CHECKS.get(name)
        if fn is None:
            print(f"  FAIL: unknown check `{name}`. valid: {sorted(CHECKS)} / all")
            failed += 1
            continue
        try:
            fn()
        except SystemExit as exc:
            if exc.code != 0:
                failed += 1
    if not targets or targets == ["all"]:
        return 0 if failed == 0 else 1
    return failed


if __name__ == "__main__":
    raise SystemExit(main() or 0)
