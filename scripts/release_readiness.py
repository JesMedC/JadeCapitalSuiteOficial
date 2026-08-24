#!/usr/bin/env python3
from __future__ import annotations

import hashlib
import json
import re
import subprocess
from pathlib import Path
from typing import Any, Callable

TAG = "v1.1.0-rc1"
MARKER = re.compile(r"^<!-- openspec-archive: ([a-zA-Z0-9._-]+) -->$", re.MULTILINE)
Run = Callable[..., subprocess.CompletedProcess[str]]


class ReadinessError(RuntimeError):
    pass


class RepositoryRootError(ReadinessError):
    pass


class ConsistencyError(ReadinessError):
    pass


class DeliveryStateError(ReadinessError):
    pass


def _execute(run: Run, command: list[str]) -> subprocess.CompletedProcess[str]:
    try:
        return run(command, capture_output=True, text=True, timeout=30, check=False)
    except (FileNotFoundError, subprocess.TimeoutExpired, OSError) as error:
        raise DeliveryStateError(f"lookup unavailable: {' '.join(command)}: {error}") from error


def resolve_repository_root(candidate: Path | str, *, run: Run = subprocess.run) -> Path:
    supplied = Path(candidate).expanduser()
    if not supplied.is_dir():
        raise RepositoryRootError(f"repository root is missing or not a directory: {supplied}")
    supplied = supplied.resolve()
    result = _execute(run, ["git", "-C", str(supplied), "rev-parse", "--show-toplevel"])
    if result.returncode != 0:
        raise RepositoryRootError(f"not a Git repository root: {supplied}")
    canonical = Path(result.stdout.strip()).resolve()
    if canonical != supplied:
        raise RepositoryRootError(f"supplied path is outside or below repository root: {supplied}")
    required = (canonical / "CHANGELOG.md", canonical / "openspec" / "changes" / "archive")
    if not all(path.exists() for path in required):
        raise RepositoryRootError(f"wrong repository root: {canonical}")
    return canonical


def _manifest_archives(path: Path) -> tuple[str, ...]:
    try:
        document = json.loads(path.read_text(encoding="utf-8"))
    except (OSError, json.JSONDecodeError) as error:
        raise ConsistencyError(f"invalid archive manifest {path}: {error}") from error
    if not isinstance(document, dict) or document.get("schema_version") != 1:
        raise ConsistencyError("archive manifest schema_version must equal 1")
    archives = document.get("archives")
    if not isinstance(archives, list) or not all(isinstance(item, str) for item in archives):
        raise ConsistencyError("archive manifest archives must be a string list")
    if any(not item or Path(item).name != item for item in archives):
        raise ConsistencyError("archive manifest keys must be non-empty directory names")
    if archives != sorted(set(archives)):
        raise ConsistencyError("archive manifest keys must be unique and sorted")
    return tuple(archives)


def validate_archive_changelog(root: Path | str) -> tuple[str, ...]:
    root_path = Path(root).resolve()
    archive_root = root_path / "openspec" / "changes" / "archive"
    manifest_path = root_path / "openspec" / "archive-manifest.json"
    changelog_path = root_path / "CHANGELOG.md"
    if not archive_root.is_dir() or not changelog_path.is_file():
        raise ConsistencyError(f"wrong repository root: {root_path}")

    actual = tuple(sorted(path.name for path in archive_root.iterdir() if path.is_dir()))
    manifest = _manifest_archives(manifest_path)
    claims = tuple(MARKER.findall(changelog_path.read_text(encoding="utf-8")))
    if claims != tuple(sorted(set(claims))):
        raise ConsistencyError("CHANGELOG archive claims must be unique and sorted")

    actual_set, manifest_set, claim_set = set(actual), set(manifest), set(claims)
    failures: list[str] = []
    failures.extend(f"archive is unlisted by manifest: {key}" for key in sorted(actual_set - manifest_set))
    failures.extend(f"changelog claim lacks an archive: {key}" for key in sorted(claim_set - actual_set))
    failures.extend(f"manifest archive has no directory: {key}" for key in sorted(manifest_set - actual_set))
    failures.extend(f"manifest archive lacks changelog claim: {key}" for key in sorted(manifest_set - claim_set))
    failures.extend(f"changelog claim is absent from manifest: {key}" for key in sorted(claim_set - manifest_set))
    if failures:
        raise ConsistencyError("; ".join(failures))
    return manifest


def _github_repository(remote: str) -> str:
    match = re.fullmatch(r"(?:git@github\.com:|https://github\.com/)([^/]+/[^/]+?)(?:\.git)?", remote.strip())
    if not match:
        raise DeliveryStateError("GitHub publication lookup unavailable: origin is not a GitHub repository")
    return match.group(1)


def _require_absent_delivery(root: Path, run: Run) -> str:
    local = _execute(run, ["git", "-C", str(root), "show-ref", "--verify", "--quiet", f"refs/tags/{TAG}"])
    if local.returncode == 0:
        raise DeliveryStateError(f"local tag already exists: {TAG}")
    if local.returncode != 1:
        raise DeliveryStateError("local tag lookup unavailable")

    remote = _execute(run, ["git", "-C", str(root), "ls-remote", "--exit-code", "--tags",
                            "origin", f"refs/tags/{TAG}"])
    if remote.returncode == 0:
        raise DeliveryStateError(f"remote tag already exists: {TAG}")
    if remote.returncode != 2:
        raise DeliveryStateError(f"remote tag lookup unavailable: {remote.stderr.strip()}")

    remote_url = _execute(run, ["git", "-C", str(root), "remote", "get-url", "origin"])
    if remote_url.returncode != 0:
        raise DeliveryStateError("GitHub release lookup unavailable: origin URL lookup failed")
    repository = _github_repository(remote_url.stdout)
    publication = _execute(run, ["gh", "api", "--include", f"repos/{repository}/releases/tags/{TAG}"])
    publication_output = f"{publication.stdout}\n{publication.stderr}"
    if publication.returncode == 0:
        raise DeliveryStateError(f"GitHub release already exists: {TAG}")
    if not re.search(r"(?:HTTP(?:/\S+)?\s+404|HTTP 404)", publication_output):
        raise DeliveryStateError(f"GitHub release lookup unavailable: {publication.stderr.strip()}")
    return repository


def build_rc_evidence(root: Path | str, sha: str, output: Path | str, *,
                      run: Run = subprocess.run) -> dict[str, Any]:
    canonical = resolve_repository_root(root, run=run)
    archives = validate_archive_changelog(canonical)
    if not re.fullmatch(r"[0-9a-f]{40}", sha):
        raise DeliveryStateError("GITHUB_SHA must be a lowercase 40-character commit SHA")
    head = _execute(run, ["git", "-C", str(canonical), "rev-parse", "HEAD"])
    if head.returncode != 0 or head.stdout.strip() != sha:
        raise DeliveryStateError("GITHUB_SHA does not identify current HEAD")
    repository = _require_absent_delivery(canonical, run)

    manifest = canonical / "openspec" / "archive-manifest.json"
    evidence: dict[str, Any] = {
        "schema": "jadecapital.rc-readiness/v1",
        "tag": TAG,
        "commit": sha,
        "repository": repository,
        "archive_manifest_sha256": hashlib.sha256(manifest.read_bytes()).hexdigest(),
        "archive_count": len(archives),
        "delivery": {"local_tag": "absent", "remote_tag": "absent", "github_release": "absent"},
    }
    destination = Path(output)
    if not destination.is_absolute():
        destination = canonical / destination
    destination = destination.resolve()
    if canonical not in destination.parents:
        raise DeliveryStateError(f"evidence output must remain inside repository root: {destination}")
    destination.parent.mkdir(parents=True, exist_ok=True)
    temporary = destination.with_suffix(f"{destination.suffix}.tmp")
    temporary.write_text(json.dumps(evidence, indent=2, sort_keys=True) + "\n", encoding="utf-8")
    temporary.replace(destination)
    return evidence
