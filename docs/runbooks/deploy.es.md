# Runbook de despliegue a producción

> Wave 10 slice 10.2 — autor: sdd-apply (PR #38).
> Espejo en español de `docs/runbooks/deployment.md`. Mantenerlos en sincronía.

## 1. Prerrequisitos

- VM Linux (Ubuntu 22.04 LTS recomendado) o instancia cloud con
  Docker Engine 25+ + Docker Compose v2 (alineado con el runner del CI en PR #37).
- Registro DNS A apuntando a la IP pública de la VM (CNAME aceptado
  para subdominios).
- Mínimo 4 GB RAM + 20 GB de disco (la stack en régimen usa ~1.5 GB;
  el resto es para cachés + backups nocturnos).
- HTTPS saliente hacia GitHub Container Registry o el registro privado
  donde se alojen las imágenes.
- TLS lo gestionan los sidecars `nginx` + `certbot` incluidos en
  `docker-compose.prod.yml`. La Wave 10 slice 10.3 migrará a Caddy
  con TLS automático; certbot queda como camino de respaldo.

## 2. Primer despliegue

### 2.1 Clonar + checkout

```bash
git clone git@github.com:JesMedC/JadeCapitalSuiteOficial.git /srv/jadecapital
cd /srv/jadecapital
git checkout feature/wave10-docs-observability   # = v1.0.0 cuando aterrice 10.6
```

### 2.2 Sembrar secretos

```bash
mkdir -p infrastructure/secrets
umask 077

# Cada secreto vive en un archivo separado. Rotar = reescribir el
# archivo y reiniciar el contenedor de la API.

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

# mailgun_api_key, stripe_api_key, stripe_webhook_secret: copiar desde
# el gestor de contraseñas. Nunca hacer eco en el historial de shell.
```

> ⚠ Los placeholders commiteados en `infrastructure/secrets/*.txt`
> existen solo para que el script de validación tenga algo que leer;
> **reemplazá cada uno** por el valor real antes del deploy.

### 2.3 Bootstrap de TLS (camino certbot)

```bash
docker compose -f docker-compose.prod.yml up -d nginx postgres redis minio
docker compose -f docker-compose.prod.yml run --rm certbot certonly \
    --webroot -w /var/www/certbot \
    -d jadecapital.example.com \
    --email ops@jadecapital.example.com \
    --agree-tos --no-eff-email
```

### 2.4 Arranque de la API

```bash
docker compose -f docker-compose.prod.yml build api frontend
docker compose -f docker-compose.prod.yml up -d api frontend
```

### 2.5 Smoke test

```bash
docker compose -f docker-compose.prod.yml ps
./scripts/wave5-smoke.sh https://jadecapital.example.com
```

## 3. Despliegue rutinario

```bash
cd /srv/jadecapital
git pull --rebase origin feature/wave10-docs-observability
docker compose -f docker-compose.prod.yml build api frontend
docker compose -f docker-compose.prod.yml up -d --no-deps api frontend
```

## 4. Rotación de secretos

```bash
NEW=$(openssl rand -base64 64)
echo "$NEW" > infrastructure/secrets/jwt_access_token_secret.txt.new
mv -f infrastructure/secrets/jwt_access_token_secret.txt.new \
      infrastructure/secrets/jwt_access_token_secret.txt

docker compose -f docker-compose.prod.yml restart api
```

## 5. Respuesta a incidentes

### 5.1 Stack caída

```bash
docker compose -f docker-compose.prod.yml ps
docker compose -f docker-compose.prod.yml logs --tail=200 api
docker compose -f docker-compose.prod.yml logs --tail=200 postgres
docker compose -f docker-compose.prod.yml logs --tail=200 certbot
```

### 5.2 Rollback

Ver `docs/runbooks/rollback.md`.

### 5.3 Corrupción de base de datos

Ver `docs/runbooks/disaster-recovery.md` (llega con la slice 10.4).

## 6. Hooks de monitoreo

- Liveness: `GET /health/live` (200 siempre que el proceso esté vivo).
- Readiness: `GET /health/ready` (200 sólo si PG + Redis responden).
- Logs estructurados: stdout (Serilog `CompactJsonFormatter` en `Production`).
- OpenTelemetry + Sentry llegan con la slice 10.6.
