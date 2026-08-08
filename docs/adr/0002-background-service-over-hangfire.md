# ADR-0002: BackgroundService para jobs en lugar de Hangfire (V1)

**Estado:** Aceptado
**Fecha:** 2026-08-07

## Contexto

`JadeCapital.Host.csproj` declara `Hangfire.AspNetCore 1.8.14`. El código actual tiene:
- `CleanupExpiredRefreshTokensJob` registrado en DI (scoped).
- Comentarios en `Program.cs` (líneas 124-128) que deshabilitan explícitamente Hangfire en V1.
- El job **nunca se ejecuta**, así que los refresh tokens expirados se acumulan en la BD indefinidamente.

Hangfire aporta: persistencia de jobs en Postgres, panel de admin, retry, scheduling avanzado. Pero requiere:
- Schema adicional en Postgres (`hangfire.*`).
- Configuración de storage.
- Más superficie de debugging y mantenimiento.

## Decisión

**Reemplazar Hangfire por un `BackgroundService` simple de .NET** (`IHostedService` con loop). Para V1, el único job necesario es `CleanupExpiredRefreshTokensJob` — un loop periódico que elimina tokens expirados hace más de X días.

- Eliminar la dependencia de `Hangfire.AspNetCore` del Host csproj (y de `Shared.Infrastructure` si corresponde).
- Crear `RefreshTokenCleanupService : BackgroundService` en `src/2.Modules/Identity/JadeCapital.Identity.Infrastructure/BackgroundJobs/`.
- Intervalo configurable vía `JwtOptions.CleanupIntervalHours` (default 6h).
- Idempotente: usa `DELETE FROM identity.refresh_tokens WHERE expires_at < @now` con `LIMIT` para no bloquear la BD.
- Logging estructurado de cuántos tokens se eliminaron por corrida.

## Consecuencias

**Positivas:**
- Cero dependencias adicionales. Menos superficie.
- Sin schema extra en Postgres.
- Funciona out-of-the-box sin configuración más allá del interval.
- Idempotencia trivial (DELETE con WHERE).

**Negativas:**
- Sin persistencia de jobs (si la API cae durante una corrida, se pierde hasta el próximo interval). Aceptable para cleanup.
- Sin panel de admin para ver jobs. Aceptable: el job es self-explanatory.
- Sin retry/backoff avanzado. No necesario para este caso.
- Sin scheduling avanzado (cron). No necesario en V1.

**Futuro:**
- Si surgen jobs con dependencias, retry complejo o scheduling crítico, reintroducir Hangfire como ADR nuevo. La interface `IHostedService` permite migrar gradualmente.

## Alternativas consideradas

1. **Reintroducir Hangfire ahora.**
   - Más completo, pero overkill para un solo job de cleanup. Sumar superficie sin justificación.

2. **Quartz.NET.**
   - Más liviano que Hangfire pero igual suma una dependencia completa. Mismo problema.

3. **Worker Service separado (otro contenedor).**
   - Útil cuando el job necesita escalar aparte. Overkill para V1.

4. **`Timer` dentro de un hosted service ad-hoc.**
   - Es lo mismo que `BackgroundService` con menos ergonomía. Descartado.