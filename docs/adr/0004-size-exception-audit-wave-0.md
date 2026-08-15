# ADR-0004 — Auditar el patrón size:exception de Wave 0 antes de aceptar su repetición

## Estado

Propuesto · 2026-08-14

## Contexto

Wave 0 (`jade-trader-os-core-portals`) cerró con todos los slices (0a-0f) mergeados en `feature/0f-billing-admin-api`. El cap blando por defecto del orquestador es **400 líneas modificadas por slice** (configurable, pero por convención se mantiene bajo para preservar review cadence y reviewer-burden protection).

| Slice | Líneas | Cap | Ratio | Estado |
|-------|--------|-----|-------|--------|
| 0a — Identity model + SQL 0006 | 1006 | 400 | 2.5× | size:exception granted |
| 0c — SMTP/API/Host + Supersession | 1258 | 400 | 3.1× | size:exception granted |
| 0d — Angular Recovery UI | 249 | 400 | 0.6× | sin exception |
| 0e — Billing aggregate + SQL 0007 | 1622 | 400 | 4.06× | size:exception granted |
| 0f — Billing Handlers + Admin.Api + Host/Authz | 1405 | 400 | 3.5× | size:exception granted |

**Patrón observado**: 4 de los 5 slices grandes pasaron 2.5×-4.06× el cap. Sólo 0d (frontend puro) cabió. Los slices que combinan varios proyectos (Domain + Application + Infrastructure + Api/Host + Tests + SQL) inevitablemente cruzan el cap.

**Riesgo**:
- Review cadence degradada: reviewers quemados por PRs de 1000-1600 líneas.
- Bugs detectados tarde: slice 0b requirió 4R review y correction_required con blockers fuera del genesis scope.
- Memory archaeology: el backup engram #419 ("slice-0c-status") muestra 3.1× con 11 commits pre-organizados para cherry-pick re-split, evidencia de que el equipo sabía que el cap se había cruzado y trató de mitigarlo a posteriori.

## Decisión

**(1) Auditar el patrón AHORA** mientras Wave 0 está fresco.

**(2) Crear una política explícita** para Wave 1 y siguientes:

- **Regla A**: Slices que combinan Domain + Application + Infrastructure + Api/Host/Migration + Tests deben **dividirse en sub-slices** (`.5`) por capa, salvo que la capa sea trivial (<100 líneas).
- **Regla B**: Si una slice cruza 2× el cap y la naturaleza del trabajo lo justifica (ej. "todo nuevo bounded context"), documentar en el slice brief **por qué** se pide size:exception y qué se mitiga.
- **Regla C**: Para size:exception, el slice brief debe incluir un **plan de cherry-pick work-unit commits** pre-construido (5-10 commits pequeños), de modo que el reviewer pueda revisar commit-por-commit en vez de los 1000-1600 net al final.
- **Regla D**: Post-Wave 0, ejecutar un Wave 0.5 housekeeping split si la cadencia de review lo demanda.

**(3) Renumerar la numeración de trabajo** para Wave 1: en vez de `1a, 1b, 1c...`, considerar `<bounded-context>-W1, .5, .6...` para que las sub-slices queden explícitas en el naming.

## Consecuencias

**Positivas**:
- Review cadence recuperada: PRs vuelven a 200-400 líneas por excepción.
- Bugs detectados antes (en el slice, no en 4R después).
- Histórico Wave 0 preserva la memoria de la lección (este ADR + backup engram).

**Negativas**:
- Wave 1 demoraría más slices nominales (más PRs por bounded context).
- Requiere disciplina de naming y brief.

## Alternativas consideradas

- **A**: Mantener el patrón actual (size:exception cada vez). Descartado: degrada reviewers y oculta bugs hasta 4R.
- **B**: Subir el cap a 1500 líneas. Descartado: elimina la red de seguridad por completo.
- **C**: Eliminar el cap. Descartado: vuelve a la versión pre-RDD donde cada PR era un dump.

## Notas de implementación

- Aplicar al próximo slice nuevo de Wave 1 (slice `1a-*` o equivalente).
- Actualizar `openspec/changes/jade-trader-os-core-portals/apply-progress.md` con este ADR como housekeeping post-Wave-0.
- Trackear el cumplimiento con un check de 4R en cada slice review.

## Referencias

- Memoria engram #413, #419, #421, #422 (slices 0a, 0c, 0e, 0f).
- `openspec/changes/jade-trader-os-core-portals/apply-progress.md` (sección post-Wave 0 housekeeping).
- `docs/engram-memory.md:554-557` — recomendación literal del apply-progress 0e.
