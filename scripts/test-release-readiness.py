#!/usr/bin/env python3
from __future__ import annotations

import json
import os
import subprocess
import sys
import tempfile
import unittest
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))

from release_readiness import (  # noqa: E402
    ConsistencyError,
    DeliveryStateError,
    RepositoryRootError,
    build_rc_evidence,
    resolve_repository_root,
    validate_archive_changelog,
)


class CommandStub:
    def __init__(self, root: Path, *, local_tag: bool = False,
                 remote_tag: bool = False, release: bool = False,
                 remote_unavailable: bool = False,
                 github_unavailable: bool = False) -> None:
        self.root = root
        self.local_tag = local_tag
        self.remote_tag = remote_tag
        self.release = release
        self.remote_unavailable = remote_unavailable
        self.github_unavailable = github_unavailable
        self.commands: list[tuple[str, ...]] = []

    def __call__(self, command: list[str], **_: object) -> subprocess.CompletedProcess[str]:
        args = tuple(command)
        self.commands.append(args)
        if args[-2:] == ("rev-parse", "--show-toplevel"):
            return self.result(args, 0, f"{self.root}\n")
        if args[-2:] == ("rev-parse", "HEAD"):
            return self.result(args, 0, f"{'a' * 40}\n")
        if "show-ref" in args:
            return self.result(args, 0 if self.local_tag else 1)
        if "remote" in args and "get-url" in args:
            return self.result(args, 0, "git@github.com:JesMedC/JadeCapitalSuiteOficial.git\n")
        if "ls-remote" in args:
            if self.remote_unavailable:
                return self.result(args, 128, stderr="fatal: unable to access remote")
            return self.result(args, 0 if self.remote_tag else 2)
        if args[:2] == ("gh", "api"):
            if self.github_unavailable:
                return self.result(args, 1, stderr="network unavailable")
            if self.release:
                return self.result(args, 0, "HTTP/2.0 200 OK\n\n{}")
            return self.result(args, 1, stderr="gh: Not Found (HTTP 404)")
        raise AssertionError(f"unexpected command: {args}")

    @staticmethod
    def result(args: tuple[str, ...], code: int, stdout: str = "",
               stderr: str = "") -> subprocess.CompletedProcess[str]:
        return subprocess.CompletedProcess(args, code, stdout, stderr)


class ReleaseReadinessTests(unittest.TestCase):
    FIXTURES = Path(__file__).resolve().parent / "fixtures" / "release-readiness"

    def setUp(self) -> None:
        self.temp_dir = tempfile.TemporaryDirectory()
        self.root = Path(self.temp_dir.name) / "repo"
        (self.root / "openspec" / "changes" / "archive" / "archive-a").mkdir(parents=True)
        (self.root / "openspec" / "changes" / "archive" / "archive-b").mkdir()
        (self.root / ".git").mkdir()
        self.write_manifest(["archive-a", "archive-b"])
        self.write_changelog(["archive-a", "archive-b"])

    def tearDown(self) -> None:
        self.temp_dir.cleanup()

    def write_manifest(self, archives: list[str]) -> None:
        path = self.root / "openspec" / "archive-manifest.json"
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_text(json.dumps({"schema_version": 1, "archives": archives}))

    def write_changelog(self, claims: list[str]) -> None:
        markers = "\n".join(f"<!-- openspec-archive: {key} -->" for key in claims)
        (self.root / "CHANGELOG.md").write_text(f"# Changelog\n{markers}\n")

    def apply_fixture(self, name: str) -> None:
        fixture = json.loads((self.FIXTURES / name).read_text())
        archive_root = self.root / "openspec" / "changes" / "archive"
        for path in archive_root.iterdir():
            path.rmdir()
        for key in fixture["actual"]:
            (archive_root / key).mkdir()
        self.write_manifest(fixture["manifest"])
        self.write_changelog(fixture["claims"])

    def test_accepts_exact_archive_manifest_changelog_sets(self) -> None:
        result = validate_archive_changelog(self.root)
        self.assertEqual(result, ("archive-a", "archive-b"))

    def test_rejects_archive_unlisted_by_manifest_and_names_key(self) -> None:
        self.apply_fixture("archive-unlisted.json")
        with self.assertRaisesRegex(ConsistencyError, "archive-b"):
            validate_archive_changelog(self.root)

    def test_rejects_changelog_claim_without_archive_and_names_claim(self) -> None:
        self.apply_fixture("changelog-claim-without-archive.json")
        with self.assertRaisesRegex(ConsistencyError, "archive-b"):
            validate_archive_changelog(self.root)

    def test_rejects_manifest_entry_without_changelog_claim(self) -> None:
        self.write_changelog(["archive-a"])
        with self.assertRaisesRegex(ConsistencyError, "archive-b"):
            validate_archive_changelog(self.root)

    def test_rejects_duplicate_and_unsorted_manifest_entries(self) -> None:
        for entries in (["archive-a", "archive-a"], ["archive-b", "archive-a"]):
            with self.subTest(entries=entries):
                self.write_manifest(entries)
                with self.assertRaises(ConsistencyError):
                    validate_archive_changelog(self.root)

    def test_resolves_relative_and_absolute_repository_roots(self) -> None:
        stub = CommandStub(self.root.resolve())
        relative = Path(os.path.relpath(self.root, Path.cwd()))
        self.assertEqual(resolve_repository_root(relative, run=stub), self.root.resolve())
        self.assertEqual(resolve_repository_root(self.root.resolve(), run=stub), self.root.resolve())

    def test_rejects_missing_outside_and_wrong_repository_roots(self) -> None:
        missing = self.root / "missing"
        outside = Path(self.temp_dir.name)
        wrong = Path(self.temp_dir.name) / "wrong"
        wrong.mkdir()
        for candidate in (missing, outside, wrong):
            with self.subTest(candidate=candidate):
                with self.assertRaises(RepositoryRootError):
                    resolve_repository_root(candidate)

    def test_builds_exact_commit_non_delivering_evidence(self) -> None:
        output = self.root / "artifacts" / "rc-readiness.json"
        stub = CommandStub(self.root.resolve())
        evidence = build_rc_evidence(self.root, "a" * 40, output, run=stub)
        self.assertEqual(evidence["tag"], "v1.1.0-rc1")
        self.assertEqual(evidence["commit"], "a" * 40)
        self.assertEqual(evidence["delivery"], {
            "local_tag": "absent", "remote_tag": "absent", "github_release": "absent"
        })
        self.assertEqual(json.loads(output.read_text()), evidence)

    def test_rejects_sha_not_equal_to_current_head(self) -> None:
        with self.assertRaisesRegex(DeliveryStateError, "current HEAD"):
            build_rc_evidence(self.root, "b" * 40, self.root / "evidence.json",
                              run=CommandStub(self.root.resolve()))

    def test_fails_closed_for_existing_delivery_or_unavailable_lookup(self) -> None:
        cases = {
            "local tag": {"local_tag": True},
            "remote tag": {"remote_tag": True},
            "GitHub release": {"release": True},
            "remote tag lookup": {"remote_unavailable": True},
            "GitHub release lookup": {"github_unavailable": True},
        }
        for message, options in cases.items():
            with self.subTest(message=message):
                output = self.root / f"{message.replace(' ', '-')}.json"
                with self.assertRaisesRegex(DeliveryStateError, message):
                    build_rc_evidence(self.root, "a" * 40, output,
                                      run=CommandStub(self.root.resolve(), **options))
                self.assertFalse(output.exists())

    def test_uses_canonical_git_c_and_exact_remote_tag_ref(self) -> None:
        stub = CommandStub(self.root.resolve())
        build_rc_evidence(self.root, "a" * 40, self.root / "evidence.json", run=stub)
        git_commands = [command for command in stub.commands if command[0] == "git"]
        self.assertTrue(all(command[1:3] == ("-C", str(self.root.resolve()))
                            for command in git_commands))
        self.assertTrue(any(command[-1] == "refs/tags/v1.1.0-rc1"
                            for command in git_commands if "ls-remote" in command))


if __name__ == "__main__":
    unittest.main(verbosity=2)
