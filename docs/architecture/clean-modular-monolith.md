# Arquitectura: Monolito Modular + Clean Architecture + Vertical Slices

## Vista de capas

```
                 ┌─────────────────────────────────────┐
                 │           Host (1.Api)              │
                 │  Composición de módulos, AuthN/Z,   │
                 │  Swagger, Scalar, ProblemDetails,  │
                 │  CORS, RateLimit                    │
                 └─────────────────────────────────────┘
                               │
        ┌──────────────────────┼──────────────────────┬──────────────────────┐
        ▼                      ▼                      ▼                      ▼
┌──────────────┐       ┌──────────────┐       ┌──────────────┐       ┌──────────────┐
│   Identity   │       │   Trading    │       │   Billing    │       │     Admin    │
│  (completo)  │       │  (scaffold)  │       │  (scaffold)  │       │  (scaffold)  │
├──────────────┤       ├──────────────┤       ├──────────────┤       ├──────────────┤
│ Api          │       │ Api          │       │ Api          │       │ Api          │
│ Application  │       │ Application  │       │ Application  │       │ Application  │
│ Infrastructure│      │ Infrastructure│      │ Infrastructure│      │ Infrastructure│
│ Domain       │       │ Domain       │       │ Domain       │       │ Domain       │
│ Contracts    │       │ Contracts    │       │ Contracts    │       │ Contracts    │
└──────────────┘       └──────────────┘       └──────────────┘       └──────────────┘
        │
        ▼
┌─────────────────────────────────────────┐
│   Shared (3.Shared)                     │
├─────────────────────────────────────────┤
│  Shared.Kernel                          │
│   - Entity<TId>, AggregateRoot           │
│   - ValueObject                          │
│   - Result<T>, Error                     │
│   - DomainException, ValidationException │
│   - DomainGuard                          │
│   - IClock, SystemClock                  │
├─────────────────────────────────────────┤
│  Shared.Infrastructure                  │
│   - ValidationBehavior<,>               │
│   - Repository base + EF Core helpers    │
│   - BackgroundService helpers           │
│   - Serilog config                       │
└─────────────────────────────────────────┘
```

> 📌 Estado real (2026-08-07): solo **Identity** está implementado de punta a punta. Trading, Billing, Admin y PublicPortal tienen csproj pero están vacíos. Ver `docs/PROJECT-STATUS.md` para el mapa completo.

## Reglas de dependencia (estrictas)

| Desde         | puede importar                                                  |
|---------------|-----------------------------------------------------------------|
| Domain        | Domain (solo Shared.Kernel)                                     |
| Application   | Domain + Shared.Kernel                                          |
| Infrastructure| Application + Domain + Shared.Kernel + Shared.Infrastructure    |
| Api           | Application + Infrastructure + Contracts                        |
| Host (1.Api)  | todos los módulos (Api layer de cada uno)                       |

Módulos **nunca** se importan entre sí directamente. Si `Trading` necesita algo de `Identity`, ambos consumen una abstracción publicada en `Shared` o `Contracts`.

## Vertical Slices por módulo

Cada feature vive en `Application/Features/<UseCase>/`:

- `XxxCommand.cs` (record `IRequest<Result<T>>`)
- `XxxValidator.cs` (FluentValidation, **invocado por `ValidationBehavior`**)
- `XxxHandler.cs` (única lógica de aplicación, devuelve `Result<T>`)
- Resultado se mapea al response en el endpoint (Api layer).

> ⚠️ El `ValidationBehavior` vive en `Shared.Infrastructure` y se registra en el pipeline MediatR del Host. Sin él, los validators son huérfanos.

Los endpoints (Minimal API) sólo delegan: `ISender.Send(command)` → mapean `Result<T>` a HTTP según `DomainGuard`.

## Tipos de resultado

`Result<T>` / `Result` para fallos esperados del dominio.
Excepciones (`DomainException`, `ValidationException`, etc.) sólo las lanza `DomainGuard.EnsureSuccess` o el `ValidationBehavior`.

## Dinero (cuando llegue Trading)

`Money` y `Currency` serán **Value Objects** en `Shared.Kernel.Money`. Todas las columnas en Postgres serán `NUMERIC(24,8)`. C# usa `decimal` exclusivamente.

Cálculos de P&L se ejecutan **siempre en el backend**. El Frontend sólo renderiza.

## Seguridad aplicada

- Contraseñas: PBKDF2 + sal aleatoria (100.000 iteraciones mínimo).
- JWT firmado HS256 con secretos distintos para access y refresh.
- Refresh tokens opacos, guardados como SHA-256.
- Bloqueo por intentos fallidos (5 default).
- `Authorization: Bearer` requerida en endpoints no públicos.
- Rate limiting por IP (`auth-strict` para auth endpoints).
- Headers de seguridad en Nginx (X-Content-Type-Options, X-Frame-Options).
- Serilog sin PII ni Authorization headers.

## Background jobs (V1)

`BackgroundService` simple (no Hangfire — ver `docs/adr/0002`). Hoy: `RefreshTokenCleanupService` corre cada N horas y purga refresh tokens expirados.

## Stack

- **Backend:** ASP.NET Core .NET 10, EF Core 9.0.1, MediatR 12.4.1, FluentValidation 11.10.0.
- **Frontend:** Angular 19 standalone (Signals), OnPush por default.
- **Persistencia:** PostgreSQL 16 (schemas por módulo), Redis 7 (caché + rate-limit).
- **Storage:** MinIO (S3 compatible) — pendiente de uso real.
- **Tests:** xUnit + FluentAssertions + NSubstitute + Testcontainers + Respawn.
- **Despliegue:** Docker Compose.

## Extensión futura a microservicios

Cada módulo está aislado por dependencias. Para extraer:

1. Mover el módulo a un nuevo `Host` con su propio `Program.cs`.
2. Mover su esquema Postgres a una BD propia o mantener compartida.
3. Reemplazar `IUserRepository` interno por HTTP client a Identity.
4. La composición actual (Host único) sigue funcionando idéntica.