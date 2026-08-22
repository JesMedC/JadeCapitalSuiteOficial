FROM postgres:16-bookworm

ARG WALG_VERSION=v3.0.9
ARG WALG_SHA256=d6795c663894d836ba20840bffe8970aed81bbe25d3151bd4951f6f9c362def9

RUN apt-get update \
    && apt-get install -y --no-install-recommends ca-certificates curl jq \
    && curl -fsSL "https://github.com/wal-g/wal-g/releases/download/${WALG_VERSION}/wal-g-pg-22.04-amd64" -o /usr/local/bin/wal-g \
    && echo "${WALG_SHA256}  /usr/local/bin/wal-g" | sha256sum -c - \
    && chmod 0755 /usr/local/bin/wal-g \
    && rm -rf /var/lib/apt/lists/*

COPY infrastructure/backup/walg-env.sh /usr/local/bin/walg-env
COPY infrastructure/backup/walg-base-backup.sh /usr/local/bin/walg-base-backup
COPY infrastructure/backup/pitr-source-entrypoint.sh /usr/local/bin/pitr-source-entrypoint
COPY infrastructure/backup/pitr-contract.sh /usr/local/lib/pitr-contract.sh

RUN chmod 0755 /usr/local/bin/walg-env /usr/local/bin/walg-base-backup /usr/local/bin/pitr-source-entrypoint
