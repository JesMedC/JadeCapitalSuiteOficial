#!/usr/bin/env python3

from __future__ import annotations

import subprocess
import tempfile
import unittest
from pathlib import Path


SCRIPT = Path(__file__).with_name("changed-csharp-files.sh")
ZERO_SHA = "0" * 40


class ChangedCsharpFilesTests(unittest.TestCase):
    def test_valid_base_reports_only_added_modified_and_renamed_csharp_files(self) -> None:
        with TemporaryRepository() as repository:
            write(repository / "kept.cs", "class Kept {}\n")
            write(repository / "deleted.cs", "class Deleted {}\n")
            write(repository / "renamed.cs", "class Renamed {}\n")
            write(repository / "notes.md", "baseline\n")
            commit(repository, "baseline")
            base_sha = git(repository, "rev-parse", "HEAD").stdout.strip()

            write(repository / "kept.cs", "class Kept { int Value => 1; }\n")
            (repository / "deleted.cs").unlink()
            git(repository, "mv", "renamed.cs", "renamed file.cs")
            write(repository / "notes.md", "changed\n")
            commit(repository, "candidate")

            self.assertEqual(run_selector(repository, base_sha), {"kept.cs", "renamed file.cs"})

    def test_zero_base_uses_parent_of_oldest_pushed_commit(self) -> None:
        with TemporaryRepository() as repository:
            write(repository / "historical.cs", "class Historical {}\n")
            commit(repository, "historical baseline")

            write(repository / "first.cs", "class First {}\n")
            commit(repository, "first pushed commit")
            oldest_pushed_sha = git(repository, "rev-parse", "HEAD").stdout.strip()

            write(repository / "nested" / "file with space.cs", "class Spaced {}\n")
            write(repository / "notes.md", "not C#\n")
            commit(repository, "second pushed commit")

            self.assertEqual(
                run_selector(repository, ZERO_SHA, oldest_pushed_sha),
                {"first.cs", "nested/file with space.cs"},
            )

    def test_missing_or_invalid_base_fails_closed(self) -> None:
        with TemporaryRepository() as repository:
            write(repository / "current.cs", "class Current {}\n")
            commit(repository, "current")

            for base_sha in ("", "not-a-valid-commit", "f" * 40):
                with self.subTest(base_sha=base_sha):
                    result = run_selector_result(repository, base_sha)
                    self.assertNotEqual(result.returncode, 0)
                    self.assertIn(b"base SHA", result.stderr)

    def test_zero_base_requires_available_oldest_pushed_commit(self) -> None:
        with TemporaryRepository() as repository:
            write(repository / "current.cs", "class Current {}\n")
            commit(repository, "current")

            for oldest_pushed_sha in ("", "not-a-valid-commit", "f" * 40):
                with self.subTest(oldest_pushed_sha=oldest_pushed_sha):
                    result = run_selector_result(repository, ZERO_SHA, oldest_pushed_sha)
                    self.assertNotEqual(result.returncode, 0)
                    self.assertIn(b"oldest pushed commit", result.stderr)

    def test_zero_base_rejects_oldest_commit_outside_head_ancestry(self) -> None:
        with TemporaryRepository() as repository:
            write(repository / "baseline.cs", "class Baseline {}\n")
            commit(repository, "baseline")
            original_branch = git(repository, "branch", "--show-current").stdout.strip()

            git(repository, "checkout", "--quiet", "-b", "unrelated")
            write(repository / "unrelated.cs", "class Unrelated {}\n")
            commit(repository, "unrelated")
            unrelated_sha = git(repository, "rev-parse", "HEAD").stdout.strip()

            git(repository, "checkout", "--quiet", original_branch)
            write(repository / "candidate.cs", "class Candidate {}\n")
            commit(repository, "candidate")

            result = run_selector_result(repository, ZERO_SHA, unrelated_sha)
            self.assertNotEqual(result.returncode, 0)
            self.assertIn(b"not an ancestor of HEAD", result.stderr)

    def test_zero_base_rejects_oldest_commit_without_a_parent(self) -> None:
        with TemporaryRepository() as repository:
            write(repository / "root.cs", "class Root {}\n")
            commit(repository, "root")
            root_sha = git(repository, "rev-parse", "HEAD").stdout.strip()

            result = run_selector_result(repository, ZERO_SHA, root_sha)
            self.assertNotEqual(result.returncode, 0)
            self.assertIn(b"has no available parent", result.stderr)

    def test_non_csharp_change_reports_an_empty_changed_set(self) -> None:
        with TemporaryRepository() as repository:
            write(repository / "unchanged.cs", "class Unchanged {}\n")
            commit(repository, "baseline")
            base_sha = git(repository, "rev-parse", "HEAD").stdout.strip()

            write(repository / "notes.md", "not C#\n")
            commit(repository, "documentation only")

            self.assertEqual(run_selector(repository, base_sha), set())


class TemporaryRepository:
    def __enter__(self) -> Path:
        self._temporary_directory = tempfile.TemporaryDirectory(prefix="jade-format-scope-")
        repository = Path(self._temporary_directory.name)
        git(repository, "init", "--quiet")
        git(repository, "config", "user.name", "CI Test")
        git(repository, "config", "user.email", "ci-test@example.invalid")
        self.repository = repository
        return repository

    def __exit__(self, exc_type, exc_value, traceback) -> None:
        self._temporary_directory.cleanup()


def write(path: Path, content: str) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(content, encoding="utf-8")


def commit(repository: Path, message: str) -> None:
    git(repository, "add", "--all")
    git(repository, "commit", "--quiet", "-m", message)


def git(repository: Path, *arguments: str) -> subprocess.CompletedProcess[str]:
    return subprocess.run(
        ["git", *arguments],
        cwd=repository,
        check=True,
        capture_output=True,
        text=True,
    )


def run_selector_result(
    repository: Path, base_sha: str, oldest_pushed_sha: str = ""
) -> subprocess.CompletedProcess[bytes]:
    return subprocess.run(
        ["bash", str(SCRIPT), base_sha, oldest_pushed_sha],
        cwd=repository,
        check=False,
        capture_output=True,
    )


def run_selector(repository: Path, base_sha: str, oldest_pushed_sha: str = "") -> set[str]:
    result = run_selector_result(repository, base_sha, oldest_pushed_sha)
    result.check_returncode()
    return {path.decode("utf-8") for path in result.stdout.split(b"\0") if path}


if __name__ == "__main__":
    unittest.main()
