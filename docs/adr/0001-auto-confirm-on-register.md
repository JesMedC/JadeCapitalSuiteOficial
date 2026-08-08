# ADR-0001: Auto-confirmar email en Register

**Estado:** Aceptado
**Fecha:** 2026-08-07

## Contexto

El módulo `Identity` define `UserStatus` con cinco estados posibles, incluyendo `PendingEmailConfirmation` que se asigna en `User.Register(...)`. El método `User.ConfirmEmail()` existe para transicionar a `Active`, pero **no es invocado por ningún flujo** del backend ni del frontend. Resultado:

- Las cuentas nuevas quedan perpetuamente en `PendingEmailConfirmation`.
- `User.CanAuthenticate()` retorna `false` para ese estado.
- El handler de `RefreshTokenCommand` rechaza el refresh (`UserStatus.NotActive`).
- **El usuario puede hacer login (recibe tokens) pero no puede refrescarlos.** Bug crítico de flujo que bloquea el uso end-to-end de la autenticación.

El proyecto **no tiene infraestructura de email configurada** (no hay SMTP, SendGrid, ni servicio de envío). El README menciona "Email confirmation" como promesa pero no hay implementación.

## Decisión

**Eliminar `PendingEmailConfirmation` del flujo de Register.** Las cuentas nuevas nacen directamente en `Active`.

- `User.Register(...)` asigna `UserStatus.Active` directamente.
- Eliminar `PendingEmailConfirmation` del enum `UserStatus` (es un estado sin uso real).
- Eliminar `User.ConfirmEmail()` (método muerto).
- Eliminar el domain event `UserEmailConfirmed` (ya no se emite).
- Actualizar tests que asumían el estado inicial.
- Actualizar la migración SQL para reflejar el nuevo default.
- Mantener el dominio event `UserRegistered` (sigue siendo relevante).

## Consecuencias

**Positivas:**
- El flujo de login/refresh funciona end-to-end desde el primer deploy.
- Se elimina un estado muerto que confundía el modelo.
- Menos código a mantener.

**Negativas:**
- No hay verificación de email. Si en el futuro queremos sumar verificación real (por compliance o seguridad), hay que reintroducir el estado y construir el flujo de email. Eso es un feature entero, no un bugfix.

**Neutras:**
- Se pierde la capacidad de detectar cuentas con emails no válidos hasta que intenten usar el sistema. Aceptable para V1 (no hay emails en juego).

## Alternativas consideradas

1. **Endpoint `POST /api/auth/confirm-email` con token de un solo uso + servicio de email.**
   - Requiere SMTP/SendGrid. Es un feature entero, no un bugfix. Diferido hasta que se justifique la inversión.

2. **Permitir auth sin confirmar (`CanAuthenticate` retorna `true` también para `PendingEmailConfirmation`).**
   - Parche. Mantiene el estado muerto. Latente para cuando se sume verificación real.

3. **Mantener el flujo actual y agregar un endpoint admin para confirmar manualmente.**
   - Hack operativo. No escala. Mala UX.