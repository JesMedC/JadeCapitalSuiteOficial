# Design: Mobile-Responsive Shell — Trader / Admin / Public

## Technical Approach

This change is **frontend-only**. It introduces a shared bottom-tab-bar component (`<jcs-mobile-nav>`), adopts it in the two authenticated shells (`trader-shell`, `admin-shell`), and adds a hamburger drawer to the public landing. The breakpoints are tokenized so future shells can re-use them. Tables get a generic `.jcs-table-scroll` wrapper utility. No backend, no .NET, no SQL.

The mobile pattern is a **bottom tab bar** (not a hamburger drawer) for authenticated shells because:

- Thumb-reach: thumb hits the bottom on a phone. Apple's HIG: bottom tab bar is the canonical mobile pattern.
- Persistent visibility: the drawer hides the nav behind an extra tap; the tab bar is always one tap away.
- iOS-native feel: dealers / traders expect iOS-style bottom tabs.

The public landing keeps a **hamburger drawer** because:

- No authenticated user, no "you are here" semantics.
- Marketing surface is content-driven; nav links are sparse.
- A bottom tab bar with 4 anonymous destinations would feel like a duplicated home screen.

The breakpoint convention aligns with framework conventions (480 / 768 / 1024 / 1280) so future Tailwind / Material interop is possible without re-tokenizing.

## Architecture Decisions

### Decision: Bottom tab bar for authenticated shells, hamburger drawer for public

**Choice**: Trader + Admin get bottom tab bar; Public landing gets hamburger drawer.

**Alternatives**: Single hamburger drawer everywhere; single bottom tab bar everywhere; bottom tab bar only when auth is present.

**Rationale**: Different audience, different intent. Authenticated users navigate between persistent surfaces (dashboard / trades / settings). Public users scan content (home / pricing / FAQ). The right pattern for each. The code path is small (one component per pattern; the shared logic is "< 768 px hides sidebar").

### Decision: Tokenize breakpoints

**Choice**: Add `--bp-mobile`, `--bp-tablet`, `--bp-desktop`, `--bp-wide` to `frontend/src/styles/tokens/_tokens.scss`.

**Alternatives**: Hard-coded breakpoints in each `@media`; SCSS variables.

**Rationale**: CSS custom properties are the project's existing convention (color, spacing, fonts are all CSS vars). Adding more hard-coded values drifts from the convention. SCSS vars would not propagate to runtime CSS for debugging. The tokens are also reusable from JS (e.g. `window.matchMedia('(min-width: 768px)')` if logic ever needs it).

### Decision: Inline SVG icons (no icon library)

**Choice**: The `<jcs-mobile-nav>` component inlines the same SVG icons used by the current `trader-shell` (dashboard, trades, calendar, settings). No icon library.

**Alternatives**: Angular Material Icons; heroicons; feather-icons via CDN.

**Rationale**: The project is dependency-light (no icon library). The existing SVGs are 18 × 18 and set in currentColor. Re-using them keeps the visual identity consistent between the sidebar and the bottom tab bar. The icons are local; no network dependency.

### Decision: `<jcs-mobile-nav>` is a dumb component

**Choice**: The component takes `items: NavItem[]` and `currentPath: string` as inputs. It does NOT subscribe to the router; the parent shell computes `currentPath` and pushes it.

**Alternatives**: Component injects `Router` and listens to `NavigationEnd`; component subscribes to `Location.path()`.

**Rationale**: The component is reusable. The parent shell knows the active segment (e.g. trader knows `/app/dashboard` vs `/app/trades`); the component just renders. This keeps the component testable in isolation (no router setup) and decouples it from the router lifecycle.

### Decision: Tables stay horizontal-scroll, not card-stack

**Choice**: The trades table (8 columns) and admin subscriptions table (6 columns) keep horizontal scroll on mobile. No card-stack transformation.

**Alternatives**: Card-stack on mobile (each row becomes a card with stacked label: value pairs).

**Rationale**: Card-stack loses column comparability. A trader scanning trades wants to compare P&L across days; a card per row makes that a vertical scroll. Horizontal scroll preserves density. The validation: 8 columns at 80 px each = 640 px minimum, which is fine on a 390 px viewport with horizontal scroll.

Sticky header on the `<thead>` ensures the user always sees the column labels while scrolling vertically.

### Decision: Refactor trader-shell inline; do not extract a HostShell

**Choice**: Modifying `trader-shell.ts` directly to include `<jcs-mobile-nav>` next to the sidebar. Same for `admin-shell.ts`. No new shared "HostShell" host.

**Alternatives**: Extract a `<jcs-host-shell>` that wraps both and configures children for nav items.

**Rationale**: The two shells have different concerns (trader has user-card + logout; admin has a header). Extracting a HostShell would either pre-emptively generalize (YAGNI) or become a leaky abstraction. The minimal change is to add the mobile-nav as a sibling to the sidebar in each shell — 5 lines of template.

### Decision: `currentPath` derived from Router events

**Choice**: Each shell subscribes to `Router.events` for `NavigationEnd`, extracts the URL, and pushes it into a `currentPath` signal. The signal is passed to `<jcs-mobile-nav>` as an input.

**Alternatives**: `Location.path()` polled in a `setInterval`; component-level Router injection.

**Rationale**: Signal-driven is the cleanest with Angular 19. `Router.events` filtered by `NavigationEnd` is the canonical pattern. No polling, no leaks if `takeUntilDestroyed` is used.

### Decision: Hamburger drawer is component-local, not a shared component

**Choice**: The hamburger drawer lives inside `landing-page.ts`. It is not extracted to a shared component.

**Alternatives**: Extract `<jcs-mobile-drawer>` to `@shared/`.

**Rationale**: The public landing is the only consumer of this pattern. Extracting now violates YAGNI. If a future page needs a drawer, the extraction is a small refactor at that point.

### Decision: No new dependencies

**Choice**: No Angular Material, no Tailwind, no new icon library. CSS custom properties + features already in modern browsers (`env(safe-area-inset-bottom)`, `position: sticky`, `overflow-x: auto`).

**Alternatives**: Add a UI library for the drawer / tabs.

**Rationale**: The change is small (~800 lines). Adding a UI library would explode the bundle and conflict with the existing token system.

## New Components

### `<jcs-mobile-nav>` — `frontend/src/app/shared/jcs-mobile-nav.ts`

```typescript
export interface NavItem {
  label: string;
  path: string;
  icon: 'dashboard' | 'trades' | 'calendar' | 'settings';
}

@Component({
  selector: 'jcs-mobile-nav',
  standalone: true,
  imports: [RouterLink, NgClass],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `…`,
  styles: [`…`],
})
export class JcsMobileNav {
  readonly items = input.required<readonly NavItem[]>();
  readonly currentPath = input.required<string>();
}
```

Behavior:
- Renders `<nav class="mobile-nav" role="navigation">` with `<a [routerLink]="item.path">` per item.
- Active tab: `aria-current="page"` when `currentPath` includes `item.path`.
- Hidden ≥ 768 px via CSS.

### Trader shell change — `frontend/src/app/features/trader/trader-shell.ts`

- Import `JcsMobileNav`.
- Add a `currentPath` signal updated by `Router.events` filtered by `NavigationEnd` (use `takeUntilDestroyed`).
- Add `<jcs-mobile-nav [items]="navItems" [currentPath]="currentPath()" />` as a sibling of `<aside>` inside `.shell`.
- Change `@media (max-width: 960px)` to `@media (max-width: 767px)` for the sidebar hide. This is the **single** breakpoint divergence the change makes (consolidates to 768).
- Add `padding-bottom: 64px` on `.main` at < 768 px so content doesn't hide under the bottom-nav.

### Admin shell change — `frontend/src/app/features/admin/admin-shell.ts`

- Same pattern as trader.
- `navItems = [{ label: 'Suscripciones', path: 'subscriptions' }]`.
- Add hamburger-equivalent for admin (none needed; the existing header navigation already works on desktop and the mobile-nav provides the only mobile nav).

### Public landing change — `frontend/src/app/features/public/landing/landing-page.ts`

- Add a `drawerOpen` signal.
- Add a hamburger button visible < 768 px.
- Add a drawer that slides in from the right with a backdrop.
- Reuse the existing `nav-links` content inside the drawer.
- Hide the inline `nav-links` < 768 px (already done).

### Utility class — `.jcs-table-scroll` + `.jcs-cards-stack-mobile`

Added to `frontend/src/styles.scss`:

```scss
.jcs-table-scroll {
  overflow-x: auto;
  -webkit-overflow-scrolling: touch;
  th { position: sticky; top: 0; background: var(--bg-card); z-index: 1; }
}

.jcs-cards-stack-mobile {
  @media (max-width: 767px) {
    grid-template-columns: 1fr !important;
    gap: var(--sp-4) !important;
  }
}
```

## Token additions

`frontend/src/styles/tokens/_tokens.scss`:

```scss
:root {
  /* existing tokens */
  --bp-mobile: 480px;
  --bp-tablet: 768px;
  --bp-desktop: 1024px;
  --bp-wide: 1280px;
}
```

## Test Strategy

| Spec | Surface | Asserts |
|---|---|---|
| `jcs-mobile-nav-renders-all-items` | `<jcs-mobile-nav>` | All items render as `<a>` with correct href |
| `jcs-mobile-nav-active-state` | `<jcs-mobile-nav>` | Item matching currentPath has `aria-current="page"` |
| `jcs-mobile-nav-no-active` | `<jcs-mobile-nav>` | No item has `aria-current` when no path matches |
| `jcs-mobile-nav-touch-targets` | `<jcs-mobile-nav>` | Each `<a>` has `min-height: 56px` (CSS class assertion) |
| `trader-shell-provides-mobile-nav` | `trader-shell` | Renders `<jcs-mobile-nav>` + hides sidebar < 768 px |
| `trader-shell-shows-sidebar-tablet` | `trader-shell` | Renders sidebar ≥ 768 px, no mobile-nav visible |
| `trader-shell-pads-content-mobile` | `trader-shell` | `.main` has `padding-bottom` on mobile |
| `admin-shell-provides-mobile-nav` | `admin-shell` | Renders `<jcs-mobile-nav>` + shows nav items |
| `admin-shell-active-state` | `admin-shell` | Active subscription link matches current path |
| `admin-shell-mobile-nav-hidden-tablet` | `admin-shell` | Mobile-nav hidden ≥ 768 px |
| `landing-hamburger-mobile` | `landing-page` | Hamburger button visible < 768 px |
| `landing-drawer-open` | `landing-page` | Drawer opens when hamburger tapped |
| `landing-drawer-close` | `landing-page` | Drawer closes on backdrop tap |
| `pricing-grid-stack-mobile` | `pricing-page` | Grid collapses to 1 column < 768 px |
| `faq-accordion-stack-mobile` | `faq-page` | Stack always vertical |
| `trades-list-table-scroll` | `trades-list` | `.table-wrap` has `overflow-x: auto` |
| `admin-list-table-scroll` | `admin-list` | Table wrapped in `.jcs-table-scroll` |

**Total: 16 new specs** (4 + 6 + 6 across 3 commits). Baseline 86 → 102 specs.

## Rollback Strategy

Each commit is independently revertable. The shared mobile-nav component is unused after commit 1 but it compiles cleanly; revert is a single-file delete. Trader-shell + admin-shell revert loses the mobile nav (back to broken, but same as pre-change). Public + tables revert loses the drawer but the public pages retain their existing 768 px rule (no worse than before).

## Open Questions

None blocking. The token values are aligned with framework conventions. The touch target size (56 px) is on the high side of the 44 px minimum — appropriate for a 4-tab layout where each tab is roughly 25% of the 390 px viewport.

## Out of Scope (Future Waves)

- Card-stack transformation of data tables (lose comparability).
- Bottom tab bar on public (no auth; hamburger is canonical).
- Angular Animations for drawer slide (CSS transitions are enough).
- PWA / service worker.
- Real-time route prefetching on hover / tap.
- i18n / locale-aware nav labels.
- Profile / settings on mobile-nav (would push to 5+ tabs; revisit).
