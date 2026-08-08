# Jade Capital Suite

SaaS profesional de **Trading Journal** para registrar, analizar y controlar operaciones en mercados financieros (Forex y opciones binarias).

## Módulos principales

1. **Portal Comercial Público** — Landing de conversión, Pricing, FAQ.
2. **Portal del Trader** — Dashboard, registro de trades, análisis, calendario P&L, configuraciones.
3. **Portal Administrador** — Gestión de usuarios, métricas SaaS, control de suscripciones.
4. **Flujo de Suscripción, Pago y Activación** — Stripe, webhooks, facturas.

> 📌 **Estado actual (2026-08-07):** solo el módulo **Identity** (auth, JWT, refresh tokens) está implementado de punta a punta. Los módulos de negocio (Trading, Billing, Admin, PublicPortal) están como scaffold (csproj vacíos). Ver [`docs/PROJECT-STATUS.md`](./PROJECT-STATUS.md) para el mapa completo y [`docs/adr/`](./adr/) para las decisiones arquitectónicas.

## Arquitectura

Monolito Modular con Clean Architecture y Vertical Slices por bounded context.

- `src/1.Api/JadeCapital.Host` — API host único que compone módulos vía `AddIdentityInfrastructure()` y `MapAuthEndpoints()`.
- `src/2.Modules/Identity/{Domain,Application,Infrastructure,Api,Contracts}` — Vertical slice Auth completo.
- `src/2.Modules/{Trading,Billing,Admin,PublicPortal}` — Scaffold (próximas verticales).
- `src/3.Shared/JadeCapital.Shared.Kernel` — Tipos base (`Money`, `Entity`, `AggregateRoot`, `ValueObject`, `Result<T>`, `Error`, `DomainGuard`, `IClock`).
- `src/3.Shared/JadeCapital.Shared.Infrastructure` — `ValidationBehavior<,>`, helpers de EF Core, `BackgroundService` helpers.

Cada módulo expone su propio `IServiceCollection Add<Module>Infrastructure()` y `IEndpointRouteBuilder Map<Module>()`. El Host los cablea. La extracción a microservicio futuro es mover un módulo a un host propio.

## Stack

| Capa            | Tecnología                                                    |
|-----------------|---------------------------------------------------------------|
| Backend         | ASP.NET Core (.NET 10), EF Core 9.0.1, MediatR 12.4.1, FluentValidation 11.10.0 |
| Persistencia    | PostgreSQL 16 (NUMERIC para dinero, schemas por módulo), Redis 7 (caché + rate-limit) |
| Auth            | JWT HS256 + Refresh Tokens rotativos, PBKDF2                   |
| Background Jobs | `BackgroundService` (sin Hangfire — ver ADR-0002)              |
| Logs            | Serilog (Structured, sin PII)                                 |
| Frontend        | Angular 19 (standalone + Signals), OnPush, SCSS               |
| Storage         | MinIO (S3 compatible) — pendiente de uso real                 |
| Proxy           | Nginx                                                         |
| Despliegue      | Docker Compose                                                |

## Reglas innegociables

- Cero `float`/`double` para dinero. **Siempre** `decimal` + `NUMERIC(24,8)` (cuando llegue Trading).
- `Money` y `Currency` serán Value Objects inmutables (pendiente de implementar en `Shared.Kernel.Money`).
- Cero lógica financiera en el Frontend (solo render).
- Cero lógica de negocio en endpoints — delegan a `ISender.Send(command)` (MediatR).
- Secrets desde variables de entorno. Nunca en código.
- Serilog con destructuring policy para no loguear PII ni secretos.
- Sin testimonios falsos, sin promesas de rentabilidad, sin estética de casino.

## Quick start

```bash
cp .env.example .env
# editar .env: secrets JWT_*_SECRET con ≥ 32 chars
docker compose up -d
# Backend:  http://localhost:8080
# Frontend: http://localhost:4200
# Swagger:  http://localhost:8080/swagger
# Scalar:   http://localhost:8080/scalar
# MinIO:    http://localhost:9001
```

## Documentación

- [`docs/PROJECT-STATUS.md`](./PROJECT-STATUS.md) — Mapa del estado actual (módulos, gaps, bugs críticos).
- [`docs/architecture/clean-modular-monolith.md`](./architecture/clean-modular-monolith.md) — Decisiones arquitectónicas y reglas de dependencia.
- [`docs/runbooks/local-dev.md`](./runbooks/local-dev.md) — Operación local, migraciones, incidentes.
- [`docs/adr/`](./adr/) — Architecture Decision Records (decisiones documentadas con contexto y consecuencias).