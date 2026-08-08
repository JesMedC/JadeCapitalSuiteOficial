# Estado actual del proyecto — JadeCapitalSuite

> **Snapshot base:** 2026-08-07 (exploración exhaustiva del código).
> **Última actualización:** 2026-08-07 (Sprint 0 cerrado — ver §0).
> Cualquier afirmación acá fue leída de los archivos; nada es supuesto.

---

## 0. Sprint 0 — cerrado el 2026-08-07

Bugs críticos y deudas arregladas en este sprint. Documentación de decisiones en `docs/adr/`.

### Bugs críticos resueltos
| # | Bug | Solución | ADR |
|---|-----|----------|-----|
| 1 | `User.ConfirmEmail` nunca invocado → cuentas en `PendingEmailConfirmation` → refresh falla → **login no funciona end-to-end** | Auto-confirm en `Register`. `User` nace `Active` con `EmailConfirmedAt` set. Eliminado `PendingEmailConfirmation` del enum, `ConfirmEmail()` y domain event `UserEmailConfirmed`. Migración SQL actualizada. Tests ajustados. | [ADR-0001](./adr/0001-auto-confirm-on-register.md) |
| 2 | FluentValidation validators huérfanos (no había `ValidationBehavior` en pipeline MediatR) | `ValidationBehavior<TRequest,TResponse>` creado en `Shared.Infrastructure` y registrado en el Host. Validators ahora se ejecutan. | — |
| 3 | Frontend esperaba `tier` que el backend no devolvía | `tier` eliminado de la interface `User` en frontend. `loadUser()` limpia `localStorage` viejo. UI que mostraba plan removida. | [ADR-0003](./adr/0003-remove-tier-field-from-frontend-user.md) |
| 4 | `/api/trades` 404 | Resuelto a nivel de Sprint 0. **Quedó pendiente**: el endpoint no existe (módulo Trading es scaffold). Se resolvió el `tier` que crash en el cast, pero el 404 persiste. | — |
| 5 | Refresh token no se renovaba automáticamente en frontend | `errorInterceptor` ahora intenta `AuthState.refresh$()` antes de desloguar. Si el refresh funciona, reintenta la request. Excluye `/api/auth/*` para evitar loop infinito. Comparte la llamada HTTP entre requests concurrentes con `shareReplay`. | — |
| 6 | Hangfire apagado → refresh tokens expirados se acumulaban | `BackgroundService` (`RefreshTokenCleanupService`) con loop infinito + `ExecuteDeleteAsync` batch de 1000. Intervalo configurable vía `Jwt:CleanupIntervalHours` (default 6h). | [ADR-0002](./adr/0002-background-service-over-hangfire.md) |
| 7 | `RefreshTokenTtlDays` config ignorado (hardcode 14 días) | `_jwtOptions.RefreshTokenTtlDays` inyectado en `RegisterUserHandler`, `LoginHandler`, `RefreshTokenHandler`. | — |

### Smells corregidos
- `SystemClock` duplicado consolidado en `Shared.Kernel.Time`. JwtTokenService usa `IClock` del Kernel.
- `JwtOptions` registrado una sola vez (Host). Eliminado el registro duplicado en `IdentityModuleRegistration`.
- `Hangfire.AspNetCore` y compañía eliminados de csproj.
- Rate-limit policies aplicadas a endpoints `/api/auth/login`, `/register`, `/refresh` (`.RequireRateLimiting("auth-strict")`).
- README, `docs/architecture/clean-modular-monolith.md` y `docs/runbooks/local-dev.md` actualizados con paths reales y stack real (.NET 10).
- `docs/adr/` creado con los 3 ADRs arriba.

### Deuda que queda viva
- **Integration tests: 4/11 pasan, 7/11 fallan con bug de aplicación real.** El infra quedó resuelto (4 tests E2E de Health, RateLimit y Swagger pasan limpio). Los 7 fallos son todos en `AuthFlowTests` y revelan un **bug de ordenamiento/transaction en `RegisterUserHandler`**: `INSERT INTO refresh_tokens` viola la FK `fk_refresh_tokens_user` porque el `User` insertado no es visible al insert del `RefreshToken` dentro de la misma transacción. Hipótesis: dos `SaveChangesAsync` separados (uno por `User`, otro por `RefreshToken`) o falta de `IUnitOfWork.SaveChangesAsync` único. **Pendiente para Sprint 0.5** — fuera del scope explícito de "refactor para destrabar infra".
- `Money`/`Currency` value objects (necesarios para Sprint 1 / Trading).
- Multi-tenant, soft-delete, seeders, OpenTelemetry.
- Sin tests frontend (jest declarado pero sin config).
- Sin CI/CD.
- **Bugs preexistentes descubiertos durante la investigación:**
  - `IClock` declarado en dos lugares (`Shared.Kernel.Time` y `Identity.Application.Abstractions`). El agent creó `SystemClockAdapter` como puente pero la solución correcta es consolidar.
  - `<ProjectReference>` roto en `IntegrationTests.csproj` (apuntaba 2 niveles arriba, necesitaba 3).
  - `JadeCapital.slnx.disabled` (archivo legacy vacío, no usado).

### Verificación
- `dotnet build JadeCapital.slnx` → 0 errores, 1 warning pre-existente (Npgsql version conflict MSB3277).
- `dotnet test JadeCapital.slnx` → 106 unit pass, 4/11 integration pass (Health x2, Swagger, RateLimit), 7/11 integration fail (FK bug, documentado en deuda).
- `npm run build` (frontend) → OK.

### Git
- `git init` hecho en Sprint 0. Branch `main`.
- `user.name = "Jesus"`, `user.email = "jmedinac25@gmail.com"`.
- Commit inicial con Sprint 0.

---

## TL;DR (snapshot al 2026-08-07)

Esqueleto de **monolito modular .NET 10 / Angular 19** con buenas decisiones arquitectónicas en docs y **Sprint 0 cerrado** (auth flow end-to-end funciona, infra consolidada, ADRs documentados, integration tests parcialmente habilitados). Solo el módulo Identity está implementado de punta a punta. El resto (Trading, Billing, Admin, PublicPortal) son csproj vacíos. El Host no compone módulos — wirea Identity directo. **Bug residual en `RegisterUserHandler`** (FK violation al insertar refresh_token) descubierto por los integration tests; pendiente para Sprint 0.5.

---

## 1. Stack real (leído de csproj / package.json / Dockerfile)

| Capa | Tecnología | Versión | Fuente |
|------|-----------|---------|--------|
| Backend SDK | .NET | `net10.0` | `Directory.Build.props` |
| Backend runtime Docker | `mcr.microsoft.com/dotnet/aspnet` | `10.0` | `backend/Dockerfile` |
| EF Core | `Microsoft.EntityFrameworkCore` | `9.0.1` | csproj |
| EF Core Npgsql | `Npgsql.EntityFrameworkCore.PostgreSQL` | `9.0.4` | csproj |
| MediatR | `12.4.1` | csproj |
| FluentValidation | `11.10.0` | csproj |
| JWT Bearer | `Microsoft.AspNetCore.Authentication.JwtBearer` | `9.0.0` | Host csproj |
| OpenAPI | Swashbuckle `6.6.2` + Scalar `1.2.40` | Host csproj |
| Serilog | `Serilog.AspNetCore` `9.0.0` | Host csproj |
| Hangfire | `1.8.14` — **declarado pero NO cableado en V1** | Host csproj |
| Redis | `Microsoft.Extensions.Caching.StackExchangeRedis` | `9.0.0` | Shared.Infra csproj |
| PostgreSQL | `postgres:16-alpine` | docker-compose |
| Redis | `redis:7-alpine` | docker-compose |
| MinIO | `quay.io/minio/minio:latest` | docker-compose |
| Stripe | `Stripe.net` `47.0.0` — **declarado, NO usado** | Billing csproj |
| MinIO SDK | `Minio` `6.0.5` — **declarado, NO usado** | Shared.Infra csproj |
| Frontend | Angular | `^19.0.0` | `frontend/package.json` |
| Node build | `node:22-alpine` | `frontend/Dockerfile` |
| Nginx runtime | `nginx:1.27-alpine` | `frontend/Dockerfile` |
| Tests | xUnit `2.9.2` + FluentAssertions `7.0.0` + NSubstitute `5.3.0` | test csproj |
| Tests integration | Testcontainers `4.0.0` + Respawn `6.2.1` + Mvc.Testing `9.0.0` | IntegrationTests csproj |
| EF tooling | `dotnet-ef` `10.0.10` (en `.tools/`) | local |

> ⚠️ El README y `docs/architecture/clean-modular-monolith.md` dicen **.NET 8 LTS**. El código es **.NET 10**. Inconsistencia a corregir.

---

## 2. Estructura top-level

```
ProyectoOficialJadeCapitalSuite/
├── backend/              Dockerfile del API (multi-stage .NET 10)
├── docs/                 architecture/ + runbooks/  (SIN status)
├── frontend/             Angular 19 standalone (1338 líneas TS)
├── infrastructure/       nginx/ (NO usado), postgres/init + migrations, scripts/ (vacíos)
├── src/                  Solución .NET
│   ├── 1.Api/JadeCapital.Host/      Host único (266 líneas Program.cs)
│   ├── 2.Modules/                   Identity (✓), Trading/Billing/Admin/PublicPortal (scaffold)
│   └── 3.Shared/                   Shared.Kernel (✓), Shared.Infrastructure (scaffold)
├── tests/                IntegrationTests + 3 UnitTests (1604 líneas)
├── .tools/               dotnet-ef 10.0.10 local
├── docker-compose.yml    7 servicios: postgres, redis, minio, migrate, api, frontend
├── Directory.Build.props Targets net10.0, warnings como errores, supresiones CA masivas
├── .editorconfig
├── .env / .env.example
├── JadeCapital.slnx              Solución activa (formato XML moderno, 25 proyectos)
└── JadeCapital.slnx.disabled     Vacía, legacy
```

**No es repo git.** No hay `.git/`, ni branch, ni remote. Considerar `git init` + `.gitignore` apropiado al arrancar.

---

## 3. Solución y proyectos (25 proyectos)

### `/src/1.Api/`
- **`JadeCapital.Host`** — host único. Web SDK, Serilog, Scalar, Swashbuckle, JwtBearer, IdentityModel, Hangfire, MediatR, HealthChecks (Postgres + Redis).

### `/src/3.Shared/`
- **`JadeCapital.Shared.Kernel`** — implementada. Tipos base.
- **`JadeCapital.Shared.Infrastructure`** — **VACÍA** (0 archivos `.cs`). Declara Redis, KeyDerivation, Serilog, Hangfire, Minio, Stripe, Npgsql pero no hay clases.

### `/src/2.Modules/`

| Módulo | Estado | Proyectos | Notas |
|--------|--------|-----------|-------|
| **Identity** | ✅ Completo (vertical slice Auth) | `Identity.Domain` · `Identity.Application` · `Identity.Infrastructure` · `Identity.Api` · `Identity.Contracts` | Único módulo con código real |
| **Trading** | ❌ Scaffold | `Trading.Application` · `Trading.Domain` · `Trading.Infrastructure` · `Trading.Contracts` | 0 archivos `.cs` |
| **Billing** | ❌ Scaffold | `Billing.Application` · `Billing.Domain` · `Billing.Infrastructure` · `Billing.Contracts` | 0 archivos `.cs`. Declara Stripe.net |
| **Admin** | ❌ Scaffold | `Admin.Application` · `Admin.Infrastructure` | Sin Domain, sin API, sin Contracts |
| **PublicPortal** | ❌ Scaffold | `PublicPortal.Application` · `PublicPortal.Infrastructure` | Sin Domain, sin API, sin Contracts |

### `/tests/`
- **`JadeCapital.Api.IntegrationTests`** — xUnit + Testcontainers + Respawn + Mvc.Testing. Auth (8), Health (3), `JadeApiFactory`.
- **`JadeCapital.Identity.UnitTests`** — xUnit + NSubstitute. ~73 tests en 11 archivos.
- **`JadeCapital.Shared.Kernel.UnitTests`** — xUnit + NSubstitute. 28 tests.
- **`JadeCapital.Trading.UnitTests`** — solo csproj, **vacío**.

---

## 4. Shared / Common — `src/3.Shared/`

### `JadeCapital.Shared.Kernel` (implementado)
- `Primitives/Entity.cs` — `Entity<TId>` con `CreatedAt`/`UpdatedAt`, `Touch()`.
- `Primitives/AggregateRoot.cs` — extiende `Entity`, mantiene `DomainEvents`.
- `Primitives/IDomainEvent.cs` — interface `OccurredOn`.
- `Primitives/ValueObject.cs` — base con `GetEqualityComponents()`.
- `Results/Result.cs` — `Result<T>` + `Result` (non-generic). `IsSuccess`/`IsFailure`/`Value`/`Error`. Helpers `Success<T>`/`Failure<T>`.
- `Results/Error.cs` — `readonly record struct Error(Code, Message)`. Factories por categoría: `Validation`, `NotFound`, `Conflict`, `Unauthorized`, `Forbidden`, `Failure`, `Infrastructure`. Prefijos: `validation.`, `notfound.`, `conflict.`, `unauthorized.`, `forbidden.`, `failure.`, `infrastructure.`.
- `Exceptions/DomainException.cs` — base + `NotFoundDomainException`, `ConflictDomainException`, `UnauthorizedDomainException`, `ForbiddenDomainException`.
- `Exceptions/ValidationException.cs` — lleva `IReadOnlyList<ValidationFailure>`.
- `Validation/DomainGuard.cs` — `EnsureSuccess(Result)` mapea `Error.Code` al throw apropiado según prefijo.
- `Time/IClock.cs` — interface `IClock.UtcNow` + `SystemClock`.

> ⚠️ **`Money` y `Currency` prometidos en README y docs — NO EXISTEN.** Hay que crearlos cuando arranque Trading.
> ⚠️ **`SystemClock` duplicado**: vive en `Shared.Kernel.Time` Y en `Identity.Infrastructure.Security.JwtTokenService.cs`. No colisionan hoy (namespaces distintos), pero es smell.

### Convenciones detectadas
- C#: `TreatWarningsAsErrors=true`, `Nullable=enable`, `ImplicitUsings=enable`. CA `latest-recommended` + supresión masiva de CA1014, CA1716, CA1812, CA2007, CA1000, CA1861, CA1859, CA1725, CA1873, CA1305, CA1862, CA1806, CS0618, ASPDEPR, MSB9008 + NU1902;NU1903;NU1603;NU1605;CA1707;CA1848.
- DB: `snake_case` (`users`, `refresh_tokens`, `password_hash`, `display_name`). Schema por módulo (`identity.HasDefaultSchema("identity")`).
- Error codes: `lower.case.dotted` con prefijo de categoría + sufijo `boundedContext.entidad.detalle` (`validation.user.email_invalid`, `conflict.auth.email_already_registered`).
- TS path aliases: `@core/*`, `@shared/*`, `@features/*`, `@env/*`.
- CSS/JS prefix: `jcs-`.
- Frontend: standalone components, OnPush por default.
- Inconsistencia asimétrica: `UserStatus` se persiste como `string`, `UserRole` como `int`. Decisión deliberada pero asimétrica.

---

## 5. Módulos — estado real

### 5.1 Identity — ✅ Módulo completo (vertical slice Auth)

```
src/2.Modules/Identity/
├── JadeCapital.Identity.Domain/
│   ├── Authentication/RefreshToken.cs
│   ├── Common/IdentityDomainErrors.cs
│   └── Users/{User.cs, UserRole.cs, UserStatus.cs, UserDomainEvents.cs}
├── JadeCapital.Identity.Application/
│   ├── Abstractions/{IPasswordHasher, ITokenService, IUnitOfWork, IUserRepository}.cs
│   ├── Behaviors/PasswordPolicy.cs
│   ├── _Common/IdentityApplicationErrors.cs
│   └── Features/Auth/{Login,Logout,Refresh,Register}/{Command,Handler}.cs (+ Validator junto al Command)
├── JadeCapital.Identity.Infrastructure/
│   ├── BackgroundJobs/CleanupExpiredRefreshTokensJob.cs   ⚠️ registrado, NO se ejecuta
│   ├── DependencyInjection/IdentityModuleRegistration.cs
│   ├── Persistence/{IdentityDbContext.cs, Repositories.cs}
│   └── Security/{JwtTokenService.cs, Pbkdf2PasswordHasher.cs}
└── JadeCapital.Identity.Api/
    └── Endpoints/AuthEndpoints.cs
```

**Aggregates / Entities:**
- `User : AggregateRoot<Guid>` — factory `Register(...)`, métodos `ConfirmEmail`, `ChangePassword`, `ChangeDisplayName`, `ChangeTimezone`, `ChangeRole`, `Suspend`, `Reactivate`, `Cancel`, `RecordSuccessfulLogin`, `RecordFailedLogin`, `IsLockedOut`, `CanAuthenticate`. Estados: `PendingEmailConfirmation`, `Active`, `Suspended`, `Cancelled`, `LockedOut`. Lockout 5 intentos / 15 min.
- `RefreshToken : Entity<Guid>` — factory `Issue(...)`, `Revoke`, `IsActive`, `IsExpired`. Hash SHA-256, rotación encadenada con `ReplacedByTokenId`.

**Domain Events (8):** `UserRegistered`, `UserEmailConfirmed`, `UserPasswordChanged`, `UserRoleChanged`, `UserSuspended`, `UserReactivated`, `UserCancelled`, `UserLockedOut`.

**Commands / Handlers (MediatR):**
- `RegisterUserCommand/Handler` + `RegisterUserValidator`
- `LoginCommand/Handler` + `LoginValidator`
- `RefreshTokenCommand/Handler` + `RefreshTokenValidator`
- `LogoutCommand/Handler` (idempotente, sin validator)

**Endpoints (Minimal API, `MapGroup("/api/auth")`):**
- `POST /api/auth/register` (AllowAnonymous)
- `POST /api/auth/login` (AllowAnonymous) — ⚠️ rate-limit policy **NO aplicada al endpoint**
- `POST /api/auth/refresh` (AllowAnonymous)
- `POST /api/auth/logout` (RequireAuthorization)

**DbContext (`IdentityDbContext`):**
- Schema `identity`, tablas `users` + `refresh_tokens`. Configuración fluent con `IEntityTypeConfiguration`.
- Índices únicos `ux_users_email`, `ux_refresh_tokens_hash`. Índices `ix_refresh_tokens_user_active`, `ix_refresh_tokens_expires`.
- `Status` VARCHAR, `Role` INT.
- ⚠️ **Migración EF Core no se usa**. La DB se monta por SQL manual (`infrastructure/postgres/migrations/20260806_0001_InitialIdentitySchema.sql`).

**`IdentityApplicationErrors`:** `PasswordTooShort`, `PasswordRequiresComplexity`, `PasswordTooLong`, `RefreshTokenInvalid`, `AccessDenied`, `AccountLockedOut`.

**`IdentityDomainErrors`:** errores de invariantes del dominio (separado del Application).

### 5.2 Trading — ❌ Scaffold (0 archivos `.cs`)
- Los 4 csproj existen. No hay `Trade` Aggregate, no hay Commands, no hay Endpoints, no hay DbContext.
- **El frontend llama a `/api/trades?page=1&pageSize=20` → 404.**

### 5.3 Billing — ❌ Scaffold (0 archivos `.cs`)
- 4 csproj. Declara `Stripe.net 47.0.0` pero no se usa.
- Sin `Subscription`, `Plan`, `Invoice`, `Payment`, sin webhook receiver. README promete "Stripe, webhooks, facturas" — todo pendiente.

### 5.4 Admin — ❌ Scaffold (0 archivos `.cs`)
- Solo `Admin.Application` + `Admin.Infrastructure`. Sin Domain, sin API, sin Contracts.
- Frontend tiene `features/admin/admin-shell.ts` con placeholder "Próximamente", pero **NO enchufado a `app.routes.ts`**.

### 5.5 PublicPortal — ❌ Scaffold (0 archivos `.cs`)
- Solo `PublicPortal.Application` + `PublicPortal.Infrastructure`. Sin Domain, sin API, sin Contracts.
- Pricing hardcodeado en `frontend/.../pricing/pricing-page.ts` (constantes `PLANS = [...]`).

---

## 6. Host / API — `src/1.Api/JadeCapital.Host/`

`Program.cs` único (266 líneas). Composición:
1. Serilog desde configuración.
2. `JwtOptions` desde sección `Jwt`. ⚠️ **registrado dos veces** (también en `IdentityModuleRegistration.AddIdentityInfrastructure`).
3. JWT Bearer HS256, `RequireHttpsMetadata = !IsDevelopment()`, `ClockSkew = 30s`, claims `Sub`/`Email`/`Role`.
4. Identity module vía `services.AddIdentityInfrastructure(builder.Configuration)`.
5. MediatR registrado desde `JadeCapital.Identity.Application` (única assembly).
6. **RateLimiter con dos policies**: `auth-strict` (10 req/min/IP) y `api-general` (100 req/min/IP). ⚠️ **Creadas pero NO aplicadas con `.RequireRateLimiting(...)` a ningún endpoint.**
7. HealthChecks: `self` (live), `postgres` (ready), `redis` (ready).
8. **Hangfire DESHABILITADO en V1**. `CleanupExpiredRefreshTokensJob` registrado en DI pero **NO se schedulea**. Comentario en `Program.cs:124-128`: "V2: reintroducir Hangfire.PostgreSql + Serilog.Sinks.PostgreSQL".
9. Swagger + Scalar en Dev.
10. CORS desde `Cors:Origins` (default sin origins — `CORS_ORIGIN` env).
11. ForwardedHeaders para Nginx.
12. **Pipeline:** ForwardedHeaders → ExceptionHandler (switch por excepción → ProblemDetails) → SerilogRequestLogging → CORS → RateLimiter → AuthN → AuthZ.
13. **Endpoints:** `MapHealthChecks("/health/live", "/health/ready")` + `MapAuthEndpoints()`.

**ProblemDetails mapping (en `UseExceptionHandler`):**
- `ValidationException` → 400 con `errors` agrupados por `PropertyName`
- `NotFoundDomainException` → 404
- `ConflictDomainException` → 409
- `UnauthorizedDomainException` → 401
- `ForbiddenDomainException` → 403
- `DomainException` → 422
- default → 500

> ⚠️ **No existe un `ValidationBehavior<,>` en el pipeline de MediatR** que invoque los `*Validator`. Los validators están definidos pero **nadie los corre**. El handler valida a mano vía `DomainGuard`. Hay doble capa accidental que funciona por casualidad.

> ⚠️ El Host **no compone módulos**. El README promete `AddModules()`/`MapModules()` pero el Host hace `AddIdentityInfrastructure()` y `MapAuthEndpoints()` directo.

> ⚠️ **No hay `appsettings.json`, `appsettings.Development.json` ni `launchSettings.json`**. Toda config viene de env vars (formato `Jwt__Issuer`, `ConnectionStrings__Postgres`, etc., ver `docker-compose.yml`).

---

## 7. Frontend Angular

**Stack REAL:** Angular **19.x** (paquete `@angular/core: ^19.0.0`), standalone por default, OnPush, TS `~5.6.0`, zone.js `~0.15.0`, RxJS `~7.8.0`. Total: **1338 líneas TS**.

**Faltantes prometidos en README:**
- ❌ No hay `@angular/material`, `@ngrx/*`, `@apollo/client`.
- ❌ No hay ApexCharts ni FullCalendar — los charts del hero en `landing-page.ts` son SVG inline hechos a mano.
- ❌ **Tests:** `package.json` dice `"test": "jest"` pero no hay `jest.config.*`, ni `tsconfig.spec.json`, ni `*.spec.ts`. Cero tests frontend.

### Estructura `frontend/src/`
```
frontend/src/
├── index.html, main.ts, styles.scss, styles/tokens/_tokens.scss
├── environments/{environment.ts, environment.prod.ts}
└── app/
    ├── app.ts, app.config.ts, app.routes.ts
    ├── core/
    │   ├── api/                    ⚠️ VACÍO
    │   ├── auth/                   ⚠️ VACÍO (referenciado por `@core/auth`, no usado)
    │   ├── guards/auth.guard.ts    CanMatchFn → AuthState
    │   ├── interceptors/{auth.interceptor.ts, error.interceptor.ts}
    │   └── state/auth.state.ts     Signals-based, providedIn: 'root'
    ├── features/
    │   ├── admin/{admin.routes.ts, admin-shell.ts}   ⚠️ NO enchufado en app.routes.ts
    │   ├── auth/{auth.routes.ts, login/, register/}
    │   ├── public/{landing/ (571 líneas), pricing/, faq/}
    │   └── trader/
    │       ├── trader.routes.ts
    │       ├── trader-shell.ts
    │       ├── dashboard/dashboard.page.ts   ⚠️ consume /api/trades?page=1 → 404
    │       ├── trades/trades-list.page.ts    ⚠️ placeholder "Módulo en construcción"
    │       ├── trades-list/trades-list.page.ts   ⚠️ DUPLICADO huérfano
    │       ├── calendar/calendar.page.ts     ⚠️ placeholder
    │       ├── analytics/analytics.page.ts   ⚠️ placeholder
    │       └── settings/settings.page.ts     ⚠️ placeholder
    └── shared/{directives/, pipes/, ui/}     ⚠️ VACÍOS
```

### Routing (`app.routes.ts`)
```
''        → LandingPage
'pricing' → PricingPage
'auth'    → loadChildren → auth.routes (/login, /register)
'app'     → authGuard (CanMatch) → TraderShell + loadChildren (trader.routes)
'**'      → redirect ''
```

`features/admin/admin.routes.ts` existe pero **NO está registrado en `app.routes.ts`**.

### State management
- Sin NgRx/NgXs. Solo Signals + service (`AuthState` con `providedIn: 'root'`).
- `AuthState` usa `localStorage` (refresh + user) y `sessionStorage` (accessToken).
- Métodos: `register`, `login`, `refresh`, `logout`, `getAccessToken()`. Computed `user`, `isAuthenticated`.
- ⚠️ La interface `User` del frontend espera campo `tier` que **el backend NO devuelve**.

### Auth flow
1. Login form → `AuthState.login` → POST `/api/auth/login`.
2. `AuthState.persist(resp)` → accessToken a sessionStorage, refreshToken a localStorage, user a localStorage.
3. `authGuard` decide acceso al segmento `/app`.
4. `authInterceptor` añade `Authorization: Bearer <token>`.
5. `errorInterceptor` en 401 → `logout()` + redirect a `/auth/login`.
6. `refresh()` está definido pero **nadie lo llama**. Si el access expira (15 min), la próxima request muere con 401 sin intentar refresh.

---

## 8. Persistencia

### Migraciones
- **No hay migraciones generadas con `dotnet ef`.**
- Hay **1 migración SQL manual**:
  - `infrastructure/postgres/migrations/20260806_0001_InitialIdentitySchema.sql`
  - Schema `identity`, tablas `users` + `refresh_tokens`, índices, CHECK constraints, FKs.
- Aplicada por `infrastructure/postgres/migrate.Dockerfile` (imagen `postgres:16-alpine` ejecuta `psql` con `ON_ERROR_STOP=1`).
- El comentario SQL dice "Generada manualmente porque el entorno actual solo tiene runtime .NET 10 y dotnet-ef requiere runtime .NET 8" — **justificación obsoleta**: la herramienta local es `dotnet-ef 10.0.10`. Se podría regenerar.

### Configuración del DbContext
- Solo existe `IdentityDbContext` (`src/2.Modules/Identity/JadeCapital.Identity.Infrastructure/Persistence/IdentityDbContext.cs`).
- Registrada en `IdentityModuleRegistration` con `UseNpgsql(pgConn, npg => npg.MigrationsHistoryTable("__ef_migrations", "identity"))`.

### Seeders
- ❌ **No hay seeders**. No hay `DbSeeder`, no hay `IHostedService` de seeding.

### Convenciones de schema
- `snake_case` en columnas y tablas.
- Schemas por módulo (solo `identity` activo).
- Multi-tenant: **NO implementado** (sin `tenant_id`, sin filtros globales, sin resolución).
- Soft-delete: **NO implementado**.
- Tipos: UUID para Ids, TIMESTAMPTZ para fechas, VARCHAR(N), INTEGER para role, VARCHAR(32) para status, CHECK constraints para enums.
- Extensiones: `uuid-ossp`, `pgcrypto` (en `01-extensions.sql`). Los tests activan `citext` para emails case-insensitive — **el schema de producción NO usa `citext`**. Asimetría con tests.

---

## 9. Infraestructura

### `docker-compose.yml` — 7 servicios
1. **postgres** — `postgres:16-alpine`, puerto 5432, healthcheck `pg_isready`, volumen `postgres-data`, init `./infrastructure/postgres/init`.
2. **redis** — `redis:7-alpine`, puerto 6379, auth via `REDIS_PASSWORD`, appendonly, healthcheck `redis-cli ping`.
3. **minio** — `quay.io/minio/minio:latest`, puertos 9000 (API) + 9001 (console), volumen `minio-data`.
4. **migrate** — imagen custom desde `./infrastructure/postgres/migrate.Dockerfile`. `restart: on-failure`.
5. **api** — `./backend/Dockerfile`, depende de `migrate` (success) + `redis` (healthy) + `minio` (healthy). Puerto 8080. `ASPNETCORE_ENVIRONMENT=Production` por default.
6. **frontend** — `./frontend/Dockerfile`, depende de `api` (healthy). Puerto 4200 (mapea 80 interno).

Red: `jadenet` (bridge). Volúmenes: `postgres-data`, `redis-data`, `minio-data`.

### `infrastructure/`
```
infrastructure/
├── minio/                          VACÍO (sin seeds, sin buckets precreados)
├── nginx/                          ⚠️ Configs NO usadas por docker-compose (código muerto)
│   ├── nginx.conf
│   └── conf.d/jade.conf
├── postgres/
│   ├── init/01-extensions.sql
│   ├── migrate.Dockerfile
│   └── migrations/20260806_0001_InitialIdentitySchema.sql
└── scripts/                        VACÍO
```

### Variables de entorno esperadas (de `.env.example`)
```
POSTGRES_DB, POSTGRES_USER, POSTGRES_PASSWORD
REDIS_PASSWORD
MINIO_ROOT_USER, MINIO_ROOT_PASSWORD, STORAGE_BUCKET
ASPNETCORE_ENVIRONMENT
JWT_ISSUER, JWT_AUDIENCE, JWT_ACCESS_TOKEN_SECRET, JWT_REFRESH_TOKEN_SECRET,
JWT_ACCESS_TOKEN_TTL_MINUTES, JWT_REFRESH_TOKEN_TTL_DAYS
STRIPE_SECRET_KEY, STRIPE_WEBHOOK_SECRET
CORS_ORIGIN
```

> ⚠️ `JWT_ACCESS_TOKEN_SECRET` y `JWT_REFRESH_TOKEN_SECRET` validados a `≥ 32 chars` (en `Program.cs` y `JwtTokenService`).
> ⚠️ **`RefreshTokenTtlDays` se IGNORA** en handlers: `RegisterUserHandler` y `LoginHandler` hardcodean `_clock.UtcNow.AddDays(14)`. Deuda explícita en comentario.

### `.tools/`
- `dotnet-ef` local 10.0.10 (binario wrapper + paquete real).

---

## 10. Tests

| Proyecto | Tipo | Cobertura | Notas |
|----------|------|-----------|-------|
| `JadeCapital.Api.IntegrationTests` | Integration | Auth flow E2E (8 tests), Health (3 tests) | Testcontainers (Postgres+Redis), `JadeApiFactory`. **Respawn declarado pero NO usado.** |
| `JadeCapital.Identity.UnitTests` | Unit | ~73 tests en 11 archivos | NSubstitute para repos. Tests de domain sin mock EF. |
| `JadeCapital.Shared.Kernel.UnitTests` | Unit | 28 tests | Entity, Result, DomainGuard. |
| `JadeCapital.Trading.UnitTests` | — | — | Solo csproj, **vacío**. |

**Total:** ~100 tests, 1604 líneas de código de test. **Cero tests frontend.**

**Hallazgos en tests:**
- `JadeApiFactory` activa `citext` en BD de test, pero `migrate.Dockerfile` no → asimetría.
- Respawn en `.csproj` pero no se invoca → tests integration potencialmente se contaminan entre sí (mitigado parcialmente con `Guid.NewGuid()` para emails únicos).
- Hay un `.trx` en `tests/UnitTests/JadeCapital.Identity.UnitTests/TestResults/` con `total=1, executed=1, passed=0, failed=1` — corrida anterior fallida, no concluyente sobre estado actual.

---

## 11. Documentación existente

```
docs/
├── architecture/clean-modular-monolith.md   94 líneas — decisiones arquitectónicas (con paths obsoletos)
└── runbooks/local-dev.md                    51 líneas — quickstart + comandos desactualizados
```

**`docs/architecture/clean-modular-monolith.md`:** Capas, reglas de dependencia, Vertical Slices, `Result<T>`, `Money`/`Currency` (⚠️ no implementados), `NUMERIC(24,8)` para dinero (⚠️ no aplica), plan de extracción a microservicios.

**`docs/runbooks/local-dev.md`:** ⚠️ **Stale**. Comandos apuntan a paths que NO existen (`src/Common/JadeCapital.Domain`, `src/Host/JadeCapital.Host`). Lo real es `src/3.Shared/JadeCapital.Shared.Kernel` y `src/1.Api/JadeCapital.Host`.

**Sin READMEs** en `src/`, `tests/`, `frontend/`, `infrastructure/`, ni en módulos individuales.

**Sin ADRs** (`docs/adr/` no existe).

**Sin CI/CD** discernible (no hay `.github/workflows/`, ni `.gitlab-ci.yml`).

---

## 12. Bugs y deudas críticas

### 🔴 Bloquean el uso real

1. **FluentValidation validators huérfanos.** `RegisterUserValidator`, `LoginValidator`, `RefreshTokenValidator` existen pero **no hay `ValidationBehavior<,>` en el pipeline MediatR**. Los validators **nadie los corre**. Si llegan a invocarse, lanzarían `ValidationException`, pero el handler ya valida con `DomainGuard`. Funciona por casualidad.
2. **`User.ConfirmEmail` nunca se invoca.** Las cuentas nuevas quedan en `PendingEmailConfirmation` para siempre → `CanAuthenticate()` retorna `false` → `RefreshTokenHandler` rechaza refresh → **el usuario recién registrado puede hacer login (devuelve tokens) pero no puede refrescarlos**. Bug crítico de flujo.
3. **`tier` field inconsistency.** `AuthState` (frontend) espera `tier` en la respuesta de login. `LoginResult`/`RegisterUserResult` (backend) **no incluyen `tier`**. Cast puede fallar.
4. **`/api/trades?page=1&pageSize=20` consumido por el frontend → 404.** No hay endpoint en el backend (módulo Trading es scaffold vacío).
5. **Refresh token no se renueva automáticamente.** El frontend tiene `refresh()` definido pero **nadie lo llama**. Access token vence en 15 min → siguiente request 401 → `errorInterceptor` desloguea.
6. **`Hangfire` apagado.** `CleanupExpiredRefreshTokensJob` registrado en DI pero nunca se schedulea. Refresh tokens expirados **se acumulan para siempre** en BD.
7. **`RefreshTokenTtlDays` ignorado.** Hardcode `AddDays(14)` en `RegisterUserHandler` y `LoginHandler`. Config no se lee. Deuda explícita en comentario.

### 🟡 Smells a corregir

8. **`SystemClock` duplicado** en `Shared.Kernel.Time` y `Identity.Infrastructure.Security`.
9. **`JwtOptions` registrado dos veces** (Program.cs + IdentityModuleRegistration).
10. **Rate-limiting policies creadas pero NO aplicadas** a endpoints. `auth-strict` y `api-general` definidas en `AddRateLimiter`, pero ningún endpoint llama `.RequireRateLimiting(...)`. El test `RateLimit_Login_BlocksAfter10Attempts` pasa — verificar si es por la policy default.
11. **`features/admin/*` no wireado** en `app.routes.ts`.
12. **`features/trader/trades-list/` duplicado** huérfano.
13. **`features/trader/settings/` huérfano** (no routeado).
14. **Pricing hardcodeado** en dos lugares inconsistentes:
    - `landing-page.ts`: inicial ($9/$7), profesional ($19/$15), elite ($29/$23) — anual -20%.
    - `pricing-page.ts`: starter ($19), pro ($49), elite ($99) — sin anual.
15. **Sin `Money`/`Currency` value objects** (prometidos en docs).
16. **Sin seeders.**
17. **Sin tests frontend** (jest declarado pero sin config).
18. **Sin CI/CD.**
19. **Runbook desactualizado** (paths incorrectos).
20. **No es repo git.**
21. **README dice .NET 8** pero el código es .NET 10.
22. **No hay multi-tenant** (promesa del README).
23. **No hay soft-delete** (necesario para SaaS).
24. **Sin OpenTelemetry / métricas.**

---

## 13. Decisiones que necesitan ratificación

| # | README/docs dice | El código hace | Acción sugerida |
|---|------------------|----------------|-----------------|
| 1 | "Monolito Modular con `AddModules()`/`MapModules()`" | Host wirea Identity directo | ¿Mantener wiring directo o introducir `IModule` interface antes de sumar Trading? |
| 2 | "Money y Currency son Value Objects" | No existen | Confirmar creación en `Shared.Kernel/Money/` cuando arranque Trading |
| 3 | ".NET 8 LTS" | `net10.0` | Actualizar README + docs |
| 4 | "Vertical Slices: Command + Handler + Validator" | Validators huérfanos | Agregar `ValidationBehavior` o eliminar validators |
| 5 | "Hangfire para jobs" | Deshabilitado en V1 | ¿Reintroducir en V2 o reemplazar por otro scheduler? |
| 6 | "Stripe, webhooks, facturas" | Cero código | ¿Empezar Billing ya? |
| 7 | "RefreshTokenTtlDays configurable" | Hardcode 14 días | Bug obvio: leer config |
| 8 | "Rate limit `auth-strict`" | Policy creada, no aplicada | Bug: aplicar a `/api/auth/login|register|refresh` |
| 9 | "Schemas por bounded context" | Solo `identity` | Cuando llegue Trading: `trading.*` schema |
| 10 | "ApexCharts, FullCalendar" | No están en `package.json` | Sustituir por libs reales o borrar del README |

---

## 14. Plan sugerido para arrancar

### Sprint 0 — Higiene y desbloqueos (antes de features nuevas)
1. **`git init` + `.gitignore`** (csproj, bin/, obj/, .env, node_modules, .tools/).
2. **Crear ADRs** para las decisiones arriba (multi-tenant, módulos, scheduler, .NET 10 vs 8).
3. **Corregir README y docs** (paths reales, versión .NET 10, claims de features no implementadas).
4. **Actualizar `docs/runbooks/local-dev.md`** con paths reales.
5. **Decidir y aplicar rate-limit** a endpoints Auth (bug crítico #2 en bugs).
6. **Crear `ValidationBehavior<,>` en Shared.Infrastructure** y registrarlo en MediatR pipeline.
7. **Leer `RefreshTokenTtlDays` de config** en handlers (bug crítico #6).
8. **Crear endpoint `POST /api/auth/confirm-email`** o quitar `PendingEmailConfirmation` del estado inicial (decisión de producto: ¿se confirma por email? ¿se auto-confirma en register?). Sin resolver esto, **el login no es funcional end-to-end**.
9. **Decidir scheduler**: reintroducir Hangfire o BackgroundService simple. Por ahora el cleanup de refresh tokens es deuda viva.
10. **Implementar refresh automático en el frontend** (interceptor que llame `AuthState.refresh()` en 401 antes de logout).
11. **Eliminar `tier` de la interface `User` en frontend** o agregarlo al `LoginResult`/`RegisterUserResult` del backend.

### Sprint 1 — Trading (la vertical que más consume el frontend)
- Crear `Money` + `Currency` en `Shared.Kernel`.
- Aggregate `Trade : AggregateRoot<Guid>` con invariantes monetarias (`decimal` + validación `NUMERIC(24,8)`).
- `TradingDbContext` con schema `trading`.
- Commands: `RegisterTradeCommand`, `UpdateTradeCommand`, `CloseTradeCommand`, `DeleteTradeCommand`.
- Queries: `GetTradesQuery` (paginado), `GetTradeByIdQuery`, `GetPnlCalendarQuery`, `GetDashboardSummaryQuery`.
- Endpoints: `GET /api/trades`, `POST /api/trades`, `GET /api/trades/{id}`, `PUT /api/trades/{id}`, `DELETE /api/trades/{id}`, `GET /api/trades/dashboard`.
- Migración EF Core (o SQL manual siguiendo el patrón de Identity).
- Tests unit + integration.
- Conectar el dashboard, trades-list y analytics del frontend.

### Sprint 2 — Billing (Stripery suscripciones)
- Definir `Plan`, `Subscription`, `Invoice`, `Payment`.
- `BillingDbContext` con schema `billing`.
- Integración Stripe: products, prices, checkout session, customer portal, webhooks.
- Endpoint público `/api/billing/plans` (consumido por `pricing-page.ts`).
- Quitar los `PLANS` hardcodeados del frontend.

### Sprint 3 — Admin
- `AdminDomain` + endpoints de gestión de usuarios, planes, métricas SaaS.
- Wirear `features/admin/*` al router raíz.

### Sprint 4 — PublicPortal (si hace falta API propia)
- Si el contenido público es solo landing + FAQ + Pricing, PublicPortal puede quedarse en frontend.

---

## 15. Apéndice — números fríos

| Métrica | Valor |
|---------|-------|
| Proyectos en `.slnx` | 25 |
| Líneas en `src/` | 2.195 |
| Líneas en `tests/` | 1.604 |
| Líneas en `frontend/src/` | 1.338 |
| Archivos `.cs` totales | 38 (todos en Identity + Shared.Kernel) |
| Endpoints API expuestos | 4 (`/api/auth/{login,register,refresh,logout}`) |
| Tests escritos | ~100 |
| Migraciones DB | 1 (SQL manual) |
| ADRs | 0 |
| READMEs | 1 (raíz) |
| % módulos vacíos | 4/5 = 80% |
| Frontend tests | 0 |
| Bugs críticos bloqueantes | 7 |
| Decisiones pendientes de ratificar | 10+ |

---

## 16. Comandos útiles

```bash
# Desde la raíz del proyecto

# Compilar
dotnet build JadeCapital.slnx

# Correr tests
dotnet test JadeCapital.slnx

# Levantar todo (Docker)
cp .env.example .env
docker compose up -d

# Backend solo (sin docker, requiere Postgres + Redis locales)
cd src/1.Api/JadeCapital.Host
dotnet run

# Frontend solo
cd frontend
npm install
npm start          # http://localhost:4200
npm test           # ⚠️ falla: no hay jest config
```

---

_Documento vivo. Cualquier afirmación nueva debe basarse en lectura real del código, no en inferencia. Si algo cambia, actualizar acá._