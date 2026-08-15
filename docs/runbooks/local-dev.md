# Runbooks

## 1. Levantar entorno local

```bash
cp .env.example .env
# editar .env con secretos reales (mín. 32 chars en JWT_*_SECRET)
docker compose up -d
docker compose logs -f api
```

- API: <http://localhost:8080>
- Swagger: <http://localhost:8080/swagger>
- Scalar: <http://localhost:8080/scalar>
- Health (live): <http://localhost:8080/health/live>
- Health (ready): <http://localhost:8080/health/ready>
- Frontend: <http://localhost:4200>
- MinIO Console: <http://localhost:9001>

## 2. Resetear BD

```bash
docker compose down -v
docker compose up -d postgres migrate
docker compose restart api
```

## 3. Crear migración EF Core

> Migración SQL manual en `infrastructure/postgres/migrations/` es la fuente de verdad para V1. Si querés regenerar con `dotnet ef`, el proyecto usa **.NET 10**, y la herramienta local está en `.tools/`.

Desde la raíz del repo:

```bash
# Agregar migración (path correcto, no el de la versión vieja del doc):
dotnet ef migrations add <NombreMigracion> \
  --project src/3.Shared/JadeCapital.Shared.Kernel/JadeCapital.Shared.Kernel.csproj \
  --startup-project src/1.Api/JadeCapital.Host/JadeCapital.Host.csproj

# Para módulos que tengan DbContext propio (futuro: Trading, Billing, etc.):
dotnet ef migrations add <NombreMigracion> \
  --project src/2.Modules/<Modulo>/JadeCapital.<Modulo>.Infrastructure/JadeCapital.<Modulo>.Infrastructure.csproj \
  --startup-project src/1.Api/JadeCapital.Host/JadeCapital.Host.csproj
```

## 4. Backup Postgres

```bash
docker compose exec postgres pg_dump -U $POSTGRES_USER $POSTGRES_DB > backup-$(date +%F).sql
```

## 5. Rotación de secretos

1. Generar nuevos `JWT_ACCESS_TOKEN_SECRET` y `JWT_REFRESH_TOKEN_SECRET` (≥ 32 chars).
2. Actualizar `.env`.
3. `docker compose restart api`.
4. Todos los access tokens vigentes quedan inválidos; los refresh tokens también.

## 6. Incidente: respuesta lenta de `/api/*`

Aplica a cualquier endpoint bajo `/api/*` (trading, billing, admin, identity). El módulo Trading está implementado desde Sprint 1 (Trade/Account/Instrument + Dashboard/Calendar).

1. Verificar carga: `docker stats`.
2. Verificar queries lentas en logs Serilog.
3. Comprobar índices en Postgres: `EXPLAIN ANALYZE` de la query.
4. Verificar TTLs de Redis (caché por user).
5. Escalar `api` con `docker compose up -d --scale api=2`.

## 7. Refresh tokens no se limpian

`RefreshTokenCleanupService` corre cada `Jwt:CleanupIntervalHours` (default 6h). Verificá:

```bash
docker compose logs api | grep -i "refresh.*token.*cleanup\|cleanup.*refresh"
```

Si no aparece, el `BackgroundService` no arrancó — revisar que `Program.cs` lo registre (`builder.Services.AddHostedService<RefreshTokenCleanupService>()`).