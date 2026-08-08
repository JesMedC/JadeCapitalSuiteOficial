# ADR-0003: Eliminar campo `tier` de la interface `User` en frontend

**Estado:** Aceptado
**Fecha:** 2026-08-07

## Contexto

La interface `User` en `frontend/src/app/core/state/auth.state.ts` declara un campo `tier: 'Free' | 'Pro' | 'Elite' | string`. El backend **nunca devuelve este campo** ni en `LoginResult` ni en `RegisterUserResult` (no existe en el módulo `Identity` ni en el contrato).

El campo se consume con fallbacks tipo `user?.tier ?? 'Free'` en `trader-shell.ts`, pero el backend no lo setea. Resultado:
- `tier` siempre es `undefined` al castear la respuesta del backend a `User`.
- Cualquier código que use `user.tier` para lógica real (mostrar features, gating, etc.) operaría sobre `undefined` y mostraría 'Free' por el fallback, **silenciosamente**.

El módulo `Billing` (donde un sistema de tiers/planes tendría sentido) es **scaffold vacío** — declarar `tier` ahora es pre-optimización sin fundamento.

## Decisión

**Eliminar el campo `tier` de la interface `User` del frontend.** No agregarlo al backend. Si en el futuro se introduce un sistema de planes real (módulo `Billing`), se modela como una entidad propia (`Subscription`, `Plan`) y se expone vía su propio endpoint — no como un campo mágico en `User`.

- Quitar `tier` de la interface `User` en `auth.state.ts`.
- Quitar los fallbacks `?? 'Free'` en `trader-shell.ts` y donde se use.
- Mantener el `role` del usuario (esa sí viene del backend vía el claim `role` en el JWT).
- Decisión documentada: tiers/planes se modelarán en `Billing` cuando exista.

## Consecuencias

**Positivas:**
- Elimina un campo mentiroso que creaba falsa sensación de feature.
- Fuerza a modelar planes/tiers correctamente cuando llegue Billing.
- Quita el `?? 'Free'` que silenciaba bugs.

**Negativas:**
- Si en algún lugar del frontend se había empezado a usar `tier` para gating, hay que sacarlo. (Verificación: ningún componente actual lo usa de verdad — confirmado en audit.)

**Neutras:**
- Cuando llegue `Billing`, se introduce `Subscription` con su propio endpoint y la UI consume desde ahí.

## Alternativas consideradas

1. **Agregar `tier` al `LoginResult`/`RegisterUserResult` del backend.**
   - Sin modelo de Billing que lo sustente, el campo es ficticio. Mala práctica.

2. **Dejar `tier` con default `'Free'` y seguir.**
   - Mantiene la deuda latente. Confuso para quien venga a leer el código.

3. **Eliminar `User` completo y derivar del JWT.**
   - El JWT ya trae `sub`, `email`, `role`. Se podría leer de ahí directamente. Considerado para V2 cuando el equipo esté cómodo. Hoy es refactor que no aporta.