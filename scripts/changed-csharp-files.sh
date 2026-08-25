#!/usr/bin/env bash
set -euo pipefail

base_sha="${1:-}"
oldest_pushed_sha="${2:-}"
zero_sha="0000000000000000000000000000000000000000"

fail() {
    printf 'changed-csharp-files: %s\n' "$1" >&2
    exit 1
}

is_commit() {
    local sha="$1"
    [[ "$sha" =~ ^[0-9a-fA-F]{40,64}$ ]] \
        && git cat-file -e "${sha}^{commit}" 2>/dev/null
}

if [[ "$base_sha" == "$zero_sha" ]]; then
    if ! is_commit "$oldest_pushed_sha"; then
        fail "new-branch push has a zero base SHA, but the oldest pushed commit is missing or unavailable; pass github.event.commits[0].id as argument 2 and use fetch-depth: 0"
    fi
    if ! git merge-base --is-ancestor "$oldest_pushed_sha" HEAD; then
        fail "oldest pushed commit $oldest_pushed_sha is not an ancestor of HEAD; verify the push event commit order and checkout history"
    fi
    if ! base_sha="$(git rev-parse --verify "${oldest_pushed_sha}^1^{commit}" 2>/dev/null)"; then
        fail "oldest pushed commit $oldest_pushed_sha has no available parent; ensure the branch starts from an existing repository commit"
    fi
elif ! is_commit "$base_sha"; then
    fail "base SHA '${base_sha:-<missing>}' is malformed or unavailable; pass the event base SHA and use fetch-depth: 0"
fi

git diff --name-only --diff-filter=ACMR -z "$base_sha" HEAD -- '*.cs'
