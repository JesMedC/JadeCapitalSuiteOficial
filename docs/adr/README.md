# Architecture Decision Records

Este directorio contiene las decisiones arquitectónicas del proyecto JadeCapitalSuite en formato ADR (Architecture Decision Records).

Cada ADR documenta **una** decisión significativa: el contexto, las opciones consideradas, la decisión tomada y sus consecuencias.

## Índice

| # | Título | Estado | Fecha |
|---|--------|--------|-------|
| [0001](./0001-auto-confirm-on-register.md) | Auto-confirmar email en Register (sin flujo de verificación) | Aceptado | 2026-08-07 |
| [0002](./0002-background-service-over-hangfire.md) | `BackgroundService` para jobs en lugar de Hangfire (V1) | Aceptado | 2026-08-07 |
| [0003](./0003-remove-tier-field-from-frontend-user.md) | Eliminar campo `tier` de la interface `User` en frontend | Aceptado | 2026-08-07 |

## Convenciones

- Archivos numerados con prefijo `NNNN-titulo-en-kebab-case.md`.
- Estados: `Propuesto`, `Aceptado`, `Rechazado`, `Superseded` (referenciando al ADR que lo reemplaza).
- Cada ADR tiene secciones: **Contexto**, **Decisión**, **Consecuencias**, **Alternativas consideradas**.
- Una vez aceptado, no se edita el contenido — se crea un nuevo ADR que lo supersede.