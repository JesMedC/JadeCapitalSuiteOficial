# Release Readiness Evidence

`v1.1.0-rc1` readiness is a non-delivering CI gate. It validates the exact set of
OpenSpec archive directories against `openspec/archive-manifest.json` and the
machine-readable `openspec-archive` claims in `CHANGELOG.md`.

Run the consistency gate with:

```bash
python3 scripts/validate-release-readiness.py --root "$(git rev-parse --show-toplevel)"
```

The ordered CI job then builds `artifacts/release/v1.1.0-rc1-readiness.json` for
the exact `GITHUB_SHA`. The builder fails closed if the SHA is not `HEAD`, the
local or exact remote tag exists, the GitHub Release exists, or any lookup is
unavailable. A GitHub API 404 is the only accepted publication-absence result.

This process never creates or pushes a tag and never creates a GitHub Release.
Delivery requires a separate, explicitly authorized workflow.
