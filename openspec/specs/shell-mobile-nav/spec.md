# Mobile Shell Navigation Specification

## Purpose

Mobile-only navigation pattern for the authenticated Trader and Admin portals. Provides a bottom tab bar that surfaces the primary navigation items when the canonical sidebar is hidden by responsive breakpoints. Hidden at tablet (≥ 768 px) so the sidebar remains the single source of truth on larger screens.

## Requirements

### Requirement: Bottom tab bar visible only on mobile

The system MUST render a bottom tab bar (`<jcs-mobile-nav>`) when the viewport width is less than 768 px. The component MUST be hidden at 768 px or wider via CSS (`@media (min-width: 768px) { display: none }`). The canonical sidebar (in `trader-shell` / `admin-shell`) MUST be hidden at the same breakpoint and remain visible at 768 px or wider.

#### Scenario: iPhone 14 viewport (390 × 844)

- GIVEN a viewport of 390 px wide
- WHEN the trader shell renders
- THEN the bottom tab bar MUST be visible
- AND the sidebar MUST be hidden
- AND the user MUST be able to navigate to all primary nav items via the tab bar

#### Scenario: iPad viewport (768 × 1024)

- GIVEN a viewport of 768 px wide
- WHEN the trader shell renders
- THEN the bottom tab bar MUST be hidden
- AND the sidebar MUST be visible
- AND the existing sidebar navigation MUST be the active nav surface

### Requirement: Safe-area inset for iOS

The bottom tab bar MUST respect the iOS safe area inset via `padding-bottom: env(safe-area-inset-bottom)`. The element MUST have a non-zero visible height even on devices without a safe area (legacy Android / desktop browsers).

#### Scenario: Device with notch

- GIVEN a device that reports `env(safe-area-inset-bottom)` as a positive value
- WHEN the bottom tab bar renders
- THEN the bar's effective bottom padding MUST include the safe-area inset
- AND the home indicator MUST NOT overlap the tab labels

#### Scenario: Device without safe area

- GIVEN a device that reports `env(safe-area-inset-bottom)` as 0
- WHEN the bottom tab bar renders
- THEN the bar MUST still have a visible bottom padding (≥ 8 px)
- AND tab labels MUST remain visible

### Requirement: Touch targets ≥ 44 px

Each tab item MUST have a hit area of at least 44 × 44 px (Apple HIG / Material Design recommendation). The component MUST enforce this via `min-height` and `min-width` CSS. Tapping a tab MUST navigate to the corresponding route.

#### Scenario: Touch input on a tab

- GIVEN three tabs each labeled with a unique path
- WHEN a user taps a tab
- THEN the router MUST navigate to that path
- AND the previously active tab MUST lose its `aria-current="page"` attribute
- AND the tapped tab MUST gain `aria-current="page"`

### Requirement: Active state reflects current route

The component MUST accept a `currentPath` input and visually mark the corresponding tab as active. The `aria-current="page"` attribute MUST be present on the active tab link and absent on the others. The visual treatment MUST be distinguishable (color + background) and pass WCAG 2.1 AA contrast.

#### Scenario: Tab matching current path

- GIVEN the current path is `/app/trades`
- WHEN the nav renders
- THEN the tab with `path: trades` MUST have the active class
- AND the tab MUST declare `aria-current="page"`

#### Scenario: No tab matches current path

- GIVEN the current path is `/app/settings/profile` (no tab matches)
- WHEN the nav renders
- THEN NO tab MUST be marked active
- AND NO tab MUST have `aria-current="page"`

### Requirement: Accessibility — keyboard navigation

Each tab MUST be focusable via `Tab` key. The active tab MUST have a visible focus ring (`:focus-visible`). Pressing `Enter` on a focused tab MUST trigger navigation (default `<a>` behavior).

#### Scenario: Keyboard tab cycle

- GIVEN the bottom tab bar is rendered
- WHEN the user presses `Tab` repeatedly
- THEN focus MUST move through all tabs in DOM order
- AND focus MUST be visible on the active element

### Requirement: 4–5 nav items max

The component MUST accept a `items: NavItem[]` input. The intended contract is 4–5 items (Apple HIG: max 5 tabs in a bottom bar). The component MUST render items in the order received. Items beyond the 5th MUST still render but the layout SHALL gracefully wrap or scroll horizontally (graceful degradation, not enforced cutoff).

#### Scenario: 4 items (trader default)

- GIVEN the items array has 4 entries (Dashboard, Operaciones, Calendario, Settings)
- WHEN the nav renders
- THEN 4 tabs MUST be visible
- AND each tab MUST have equal width in the flex layout

#### Scenario: Admin with 1 item

- GIVEN the items array has 1 entry (Subscriptions)
- WHEN the nav renders
- THEN 1 tab MUST be visible
- AND the tab MUST be centered or left-aligned (style choice documented)

### Requirement: Hidden structure at tablet+

The component MUST use `display: none` (not `visibility: hidden` or `aria-hidden`) at ≥ 768 px. Navigation MUST rely solely on the sidebar at ≥ 768 px. The component MUST NOT occupy DOM space or affect layout at wider viewports.

#### Scenario: Viewport 1024 px

- GIVEN a viewport of 1024 px wide
- WHEN the nav renders
- THEN the nav element MUST have zero computed dimensions
- AND the surrounding layout MUST NOT reserve any space for the nav

### Requirement: Token-driven breakpoints

All breakpoints MUST be sourced from `frontend/src/styles/tokens/_tokens.scss`:

- `--bp-mobile: 480px`
- `--bp-tablet: 768px`
- `--bp-desktop: 1024px`
- `--bp-wide: 1280px`

The component and shells MUST use these tokens (via SCSS or CSS custom properties) rather than hard-coded values. The mobile-nav MUST trigger at viewport width < 768 px using `@media (min-width: 768px) { .mobile-nav { display: none; } }`.

#### Scenario: Token presence

- GIVEN the tokens file
- WHEN inspected
- THEN it MUST define `--bp-mobile`, `--bp-tablet`, `--bp-desktop`, `--bp-wide`
- AND the values MUST be 480px, 768px, 1024px, 1280px respectively
