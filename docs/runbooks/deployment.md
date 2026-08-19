# Production deployment runbook

> Wave 10 slice 10.2 — author: sdd-apply (PR #38).
> Applies to `docker-compose.prod.yml` + `infrastructure/Dockerfile.{api,frontend}.prod`.

## 1. Prerequisites

- Linux VM (Ubuntu 22.04 LTS recommended) or cloud instance with
  Docker Engine 25+ + Docker Compose v2 (matches CI runner in PR #37).
- DNS A record pointing to the VM's public IP (CNAME accepted for
  subdomains).
- Minimum 4 GB RAM + 20 GB disk (per-stack RAM ≈ 1.5 GB steady-state;
  the rest is for caches + nightly backups).
- Outbound HTTPS to GitHub Container Registry or your private registry
  if the images are not hosted on Docker Hub.
- TLS handled by the legacy `nginx` + `certbot` sidecars shipped in
  `docker-compose.prod.yml`. Wave 10 slice 10.3 migrates this to Caddy
  auto-TLS; the certbot path is the fallback.

## 2. First-time deploy

### 2.1 Clone + checkout

```bash
git clone git@github.com:JesMedC/JadeCapitalSuiteOficial.git /srv/jadecapital
cd /srv/jadecapital
git checkout feature/wave10-docs-observability   # = v1.0.0 once Wave 10.6 lands
```

### 2.2 Bootstrap secrets

```bash
mkdir -p infrastructure/secrets
umask 077

# Each secret is a separate file. Rotate values via the same path,
# then restart the API container (`docker compose restart api`).

cat > infrastructure/secrets/postgres_password.txt       <<EOF
$(openssl rand -base64 32)
EOF

cat > infrastructure/secrets/minio_root_user.txt         <<EOF
$(openssl rand -hex 16)
EOF

cat > infrastructure/secrets/minio_root_password.txt     <<EOF
openssl rand -base64 48
EOF

cat > infrastructure/secrets/jwt_access_token_secret.txt <<EOF
$(openssl rand -base64 64)
EOF

cat > infrastructure/secrets/jwt_refresh_token_secret.txt <<EOF
$(openssl rand -base64 64)
EOF

# mailgun_api_key, stripe_api_key, stripe_webhook_secret: copy from
# your password manager. Never echo them in shell history.
EDITOR=vim  visudo -f /etc/sudoers.d/jadecapital-ops   # lock down who can read
```

> ⚠ The placeholder files committed under `infrastructure/secrets/*.txt`
> exist only so the validate script has something to read; **replace
> each one** with the production-grade secret before deploying.

### 2.3 TLS bootstrap (certbot path)

```bash
docker compose -f docker-compose.prod.yml up -d nginx postgres redis minio
docker compose -f docker-compose.prod.yml run --rm certbot certonly \
    --webroot -w /var/www/certbot \
    -d jadecapital.example.com \
    --email ops@jadecapital.example.com \
    --agree-tos --no-eff-email
```

`--webroot` writes the ACME challenge to the volume shared with nginx;
the cert lands in `infrastructure/certbot/conf/` and is picked up by the
running nginx on its next reload.

### 2.4 First boot of the API stack

```bash
docker compose -f docker-compose.prod.yml build api frontend
docker compose -f docker-compose.prod.yml up -d api frontend
```

### 2.5 Smoke test

```bash
# Per-service healthchecks green (Docker Compose-managed):
docker compose -f docker-compose.prod.yml ps

# Custom smoke (Wave 5 / Wave 6 surface, but unchanged for prod):
./scripts/wave5-smoke.sh https://jadecapital.example.com
```

## 3. Routine deploy

```bash
cd /srv/jadecapital
git pull --rebase origin feature/wave10-docs-observability
docker compose -f docker-compose.prod.yml build api frontend
docker compose -f docker-compose.prod.yml up -d --no-deps api frontend
```

`--no-deps` keeps postgres / redis / minio running — restart only
infrastructure code, not stateful services.

## 4. Secret rotation

```bash
# Replace the value in the file (atomic write via temp + mv).
NEW=$(openssl rand -base64 64)
echo "$NEW" > infrastructure/secrets/jwt_access_token_secret.txt.new
mv -f infrastructure/secrets/jwt_access_token_secret.txt.new \
      infrastructure/secrets/jwt_access_token_secret.txt

# The container re-reads the file only on restart — secret rotation
# therefore always requires a container bounce.
docker compose -f docker-compose.prod.yml restart api
```

Existing JWTs remain valid until they expire (`AccessTokenTtlMinutes`,
default 15). Clients holding a refresh token can rotate transparently;
existing access tokens get rejected as soon as they cross `exp`.

## 5. Incident response

### 5.1 Stack is down

```bash
docker compose -f docker-compose.prod.yml ps             # which service is unhealthy?
docker compose -f docker-compose.prod.yml logs --tail=200 api
docker compose -f docker-compose.prod.yml logs --tail=200 postgres
docker compose -f docker-compose.prod.yml logs --tail=200 certbot
```

### 5.2 Stack needs rollback

See `docs/runbooks/rollback.md`.

### 5.3 Database corruption / data loss

See `docs/runbooks/disaster-recovery.md` (lands in slice 10.4).

## 6. Monitoring hooks

- Liveness: `GET /health/live` (always 200 if the process is up).
- Readiness: `GET /health/ready` (200 only when PG + Redis are reachable).
- Structured logs: stdout (Serilog with `CompactJsonFormatter` when
  `ASPNETCORE_ENVIRONMENT=Production`). Pipe to your log sink of choice.
- OpenTelemetry, Sentry, and the rest of the observability-light stack
  are scheduled for slice 10.6 (`feature/wave10-docs-observability`).
