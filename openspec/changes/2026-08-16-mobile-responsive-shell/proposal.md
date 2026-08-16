# Proposal: Mobile-Responsive Shell — Trader / Admin / Public

## Intent and Problem

Wave 1 cerró el ciclo del Trader portal en desktop pero la experiencia en mobile está rota: el sidebar del `trader-shell` se oculta por completo bajo 960 px sin reemplazo, dejando al trader sin navegación en el teléfono. El usuario reportó: *"en el portal de trader dentro del teléfono no me muestra el menú"*.

Bug verificado en código:

- `frontend/src/app/features/trader/trader-shell.ts:122-125` — `@media (max-width: 960px) { .shell { grid-template-columns: 1fr; } }` + `:140-142` — `.sidebar { display: none; }` con cero alternativa.
- `frontend/src/app/features/admin/admin-shell.ts` (33 líneas) — sin breakpoints; el `<header>` inline explota en mobile.
- `frontend/src/app/features/public/landing/landing-page.ts` (606 líneas) — `@media (max-width: 768px) { .nav-links { display: none; } }` con cero reemplazo (sin hamburger, sin drawer; los CTAs de login/registro desaparecen en mobile).
- Tablas críticas (`trades-list.page.ts`, `admin-list.page.ts`) — `trades-list` ya tiene `overflow-x: auto` en `.table-wrap` (línea 608), pero `admin-list` no y rompe en viewports angostos.

Este change cierra el gap mobile en **3 shells** (trader, admin, public) + **tablas críticas** + **utility card-stack**, sin tocar backend, sin introducir nuevos design systems, sin reescribir desktop.

## Goals

1. **Mobile nav en trader / admin / public** — Un patrón mobile-only (bottom tab bar en shells autenticados, hamburger drawer en public) reemplaza el sidebar oculto.
2. **Tablas críticas responsive** — Los listados críticos (`trades-list`, `admin-list`) funcionan en mobile vía scroll horizontal con sticky header y `min-width` por columna, o card-stack cuando la densidad de columnas lo justifique.
3. **Breakpoints consistentes** — Tokens `--bp-mobile | --bp-tablet | --bp-desktop | --bp-wide` documentados y reusados; todos los `@media` numéricos convergen a esos valores.

## Scope Boundaries

**Changed:**
- `frontend/src/app/shared/jcs-mobile-nav.ts` — nuevo standalone component (bottom tab bar) + 4 specs.
- `frontend/src/app/features/trader/trader-shell.ts` — agrega `<jcs-mobile-nav>` dentro del `.shell`, oculta sidebar solo bajo 768 px, padding-bottom en `.main` para no tapar el contenido.
- `frontend/src/app/features/admin/admin-shell.ts` — equivalente al trader pero con su nav actual (`Subscriptions`).
- `frontend/src/app/features/public/landing/landing-page.ts` — topbar pasa a hamburger + drawer en mobile.
- `frontend/src/app/features/public/pricing/pricing-page.ts` — audita/fixea el grid `repeat(3, 1fr)` → `1fr` en mobile.
- `frontend/src/app/features/trader/trades/trades-list.page.ts` — confirma `.table-wrap { overflow-x: auto }` + sticky header.
- `frontend/src/app/features/admin/subscriptions/admin-list.page.ts` — envuelve `<table>` en `.jcs-table-scroll` con `overflow-x: auto`.
- `frontend/src/styles/tokens/_tokens.scss` — agrega `--bp-mobile`, `--bp-tablet`, `--bp-desktop`, `--bp-wide`.
- `frontend/src/styles.scss` — utility `.jcs-cards-stack-mobile` + `.jcs-table-scroll`.

**Unchanged:**
- Backend (no se toca .NET / SQL / MinIO).
- Waves 0 / 1 (identity, billing, trading, risk, journal, metrics).
- Desktop layouts (sidebar, grids, spacing intactos en ≥ 768 px).
- Color tokens, font tokens, shadow tokens.
- Auth flows, login, register, password recovery.
- Stripe / billing surfaces.
- i18n / locale negotiation (futuro wave).

## Capabilities

### New Capabilities

- `shell-mobile-nav`: Bottom tab bar mobile-only para shells autenticados (trader, admin). Hidden ≥ 768 px. Safe-area-inset-bottom para iOS. Touch targets ≥ 44 px. `aria-current="page"` en tab activo. Avatar / brand integrado.
- `responsive-tables`: Envoltorio `.jcs-table-scroll` con `overflow-x: auto` + sticky `<thead>` (cuando el CSS soporte `position: sticky`) y `min-width` por columna. Cubre `trades-list` (8 columnas) y `admin-list` (6 columnas). Las tablas con > 4 columnas NO se transforman a card-stack (mantienen scroll horizontal para preservar densidad).
- `public-mobile`: Topbar → hamburger button + drawer deslizable en landing. Pricing grid 3→1 (ya estaba). FAQ stack vertical con `<details>` (ya estaba). Sin bottom-nav (público, sin auth).

### Modified Capabilities

- Ninguno. Las capabilities existentes (`trader-shell`, `admin-shell`, `public-landing`) reciben refinamiento responsive pero su contrato de demanda no cambia.

## Ownership and Approach

- **Frontend** owns `jcs-mobile-nav` (shared component), el refactor de `trader-shell` / `admin-shell`, y el responsive de public/landing + pricing.
- **Frontend** owns `responsive-tables` y la utility `.jcs-cards-stack-mobile`.
- **No backend** changes — esto es 100 % presentacional.
- El breakpoint móvil/tablet se alinea con la convención de frameworks (480 / 768 / 1024 / 1280) para interoperabilidad con futuras librerías. Documentado en design.md.

## Non-Goals

- No mobile-first redesign completo: el desktop layout existente es la fuente de verdad; mobile es adaptación.
- No bottom-tab en public (home/pricing/faq): no hay auth, no hay "perfil"; hamburger drawer es más natural.
- No PWA / service worker / offline support.
- No nuevos design systems ni Angular Material / Tailwind / Bootstrap: CSS custom properties + tokens existentes.
- No i18n / hardcoded copy en otros idiomas.
- No animaciones > 200 ms (preservar performance).
- No análisis de uso / A/B testing.

## Chained Delivery, Validation, Rollback

| Slice | Deliverable | Validate | Rollback |
|---|---|---|---|
| **1a.1** Mobile-nav base | 4 breakpoint tokens + `<jcs-mobile-nav>` standalone component + 4 jest specs | `npx jest --testPathPattern=jcs-mobile-nav` → 4 specs verde; `ng build` → 0 errores | Revert del commit; ningún shell lo usa todavía |
| **1a.2** Trader + Admin shells | `trader-shell` + `admin-shell` adoptan `<jcs-mobile-nav>`; mobile breakpoints consistentes; 6 jest specs | `npx jest --testPathPattern=trader-shell\|admin-shell` → 6 specs verde; smoke `curl` con `User-Agent: iPhone` retorna HTML con `mobile-nav` | Endpoint intacto; revert del commit |
| **1a.3** Public + tables | Landing hamburger drawer; pricing grid 1 col; `admin-list` scroll wrapper; cards stack utility; 6 jest specs | `npx jest` full → 86 baseline + 16 nuevos = 102 specs verde; smoke `curl` iPhone al portal | Revert del commit; impacto visual en mobile only |

Cada slice es un commit de ≤ 400 líneas. Total estimado ≤ 800 líneas (3 commits chained).

## Dependencies and Risks

- **Dependencias**: tokens existentes (`--sp-*`, `--bg-*`, `--text-*`), Angular 19 standalone + Signals + OnPush, path aliases `@core/* @shared/* @features/*`. Sin nuevas librerías.
- **Riesgos**:
  - **Touch target size**: iOS HIG y Material Design piden ≥ 44 px. Mitigación: `.mobile-nav a { min-height: 56px }` en tokens.
  - **Safe-area inset**: iOS notch / home indicator. Mitigación: `padding-bottom: env(safe-area-inset-bottom)` en `.mobile-nav`.
  - **Routing / active state**: `currentPath` debe ser reactivo. Mitigación: signal computado desde `Router.events` filtrado por `NavigationEnd`.
  - **Doble panel (sidebar + mobile-nav) en tablet ancho (≥ 768 px)**: sidebar visible, mobile-nav `display: none`. Verificable por spec.
  - **Tablas muy anchas en mobile**: `overflow-x: auto` permite scroll horizontal; sticky header mantiene contexto. Alternativa card-stack NO se usa para preservar densidad > 4 columnas.

## Success Criteria

- [ ] User con iPhone (Tailnet `100.86.112.15:4200`) ve un bottom tab bar funcional en `/app/dashboard` y todas las rutas del trader portal.
- [ ] `curl -H "User-Agent: Mozilla/5.0 (iPhone..." http://100.86.112.15:4200/app/dashboard` retorna HTML con `class="mobile-nav"` o equivalente.
- [ ] `@media (max-width: 768px)` activa mobile-nav; `@media (min-width: 768px)` lo oculta.
- [ ] Tabla `trades-list` scrollea horizontal en 360 px de ancho sin romper layout; header sticky mantiene visibilidad.
- [ ] Tabla `admin-list` envuelta en `.jcs-table-scroll` con `overflow-x: auto` funcional.
- [ ] Landing page en mobile: CTA Login/Registrarse visible, nav-links accesibles via hamburger drawer.
- [ ] `npx jest` final: 86 baseline + 16 nuevos = 102 specs verde.
- [ ] `ng build --configuration=development` → 0 errors, 0 warnings nuevos.
- [ ] Token `--bp-tablet: 768px` documentado y reusado en todos los `@media` nuevos.
- [ ] Touch targets ≥ 44 px (estándar iOS HIG).
