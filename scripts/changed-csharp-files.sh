#!/usr/bin/env bash
set -euo pipefail

base_sha="${1:-}"

# Event SHAs are full object IDs. An absent, malformed, or unavailable base
# falls back to the empty tree so the check remains fail closed and cannot hide
# C# files from the candidate.
if [[ ! "$base_sha" =~ ^[0-9a-fA-F]{40,64}$ ]] \
    || ! git cat-file -e "${base_sha}^{commit}" 2>/dev/null; then
    base_sha="$(git hash-object -t tree /dev/null)"
fi

git diff --name-only --diff-filter=ACMR -z "$base_sha" HEAD -- '*.cs'
