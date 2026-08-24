#!/usr/bin/env bash
set -euo pipefail
umask 077

read_secret() {
    local variable=$1
    local path=$2
    if [[ ! -r "$path" ]]; then
        printf 'required WAL-G secret is unreadable: %s\n' "$path" >&2
        exit 1
    fi
    printf -v "$variable" '%s' "$(<"$path")"
    export "$variable"
}

if [[ -n "${AWS_ACCESS_KEY_ID_FILE:-}" ]]; then
    read_secret AWS_ACCESS_KEY_ID "$AWS_ACCESS_KEY_ID_FILE"
fi
if [[ -n "${AWS_SECRET_ACCESS_KEY_FILE:-}" ]]; then
    read_secret AWS_SECRET_ACCESS_KEY "$AWS_SECRET_ACCESS_KEY_FILE"
fi
if [[ -z "${WALG_FILE_PREFIX:-}" && -z "${WALG_S3_PREFIX:-}" ]]; then
    printf 'WALG_FILE_PREFIX or WALG_S3_PREFIX is required\n' >&2
    exit 1
fi

exec /usr/local/bin/wal-g "$@"
