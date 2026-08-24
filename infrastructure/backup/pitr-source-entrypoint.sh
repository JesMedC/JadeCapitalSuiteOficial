#!/usr/bin/env bash
set -euo pipefail
install -d -o postgres -g postgres -m 0700 /wal-g
exec /usr/local/bin/docker-entrypoint.sh "$@"
