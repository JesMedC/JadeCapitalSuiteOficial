#!/usr/bin/env python3

from __future__ import annotations

import subprocess
import tempfile
import unittest
from pathlib import Path


SCRIPT = Path(__file__).with_name("changed-csharp-files.sh")


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

    def test_empty_or_invalid_base_falls_back_to_all_current_csharp_files(self) -> None:
        with TemporaryRepository() as repository:
            write(repository / "first.cs", "class First {}\n")
            write(repository / "nested" / "file with space.cs", "class Spaced {}\n")
            write(repository / "notes.md", "not C#\n")
            commit(repository, "initial candidate")

            expected = {"first.cs", "nested/file with space.cs"}
            for base_sha in ("", "not-a-valid-commit", "0" * 40):
                with self.subTest(base_sha=base_sha):
                    self.assertEqual(run_selector(repository, base_sha), expected)

    def test_head_base_reports_an_empty_changed_set(self) -> None:
        with TemporaryRepository() as repository:
            write(repository / "unchanged.cs", "class Unchanged {}\n")
            commit(repository, "baseline")
            head_sha = git(repository, "rev-parse", "HEAD").stdout.strip()

            self.assertEqual(run_selector(repository, head_sha), set())


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


def run_selector(repository: Path, base_sha: str) -> set[str]:
    result = subprocess.run(
        ["bash", str(SCRIPT), base_sha],
        cwd=repository,
        check=True,
        capture_output=True,
    )
    return {path.decode("utf-8") for path in result.stdout.split(b"\0") if path}


if __name__ == "__main__":
    unittest.main()
