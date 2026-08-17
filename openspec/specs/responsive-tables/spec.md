# Responsive Tables Specification

## Purpose

Make the critical data tables (trades list, admin subscriptions list) usable on mobile viewports (< 768 px). Apply a horizontal scroll wrapper with sticky header and `min-width` per column, preserving column density when the table has > 4 columns. Provide a card-stack utility for less dense layouts where appropriate.

## Requirements

### Requirement: Horizontal scroll wrapper for dense tables

Tables with 4 or more columns MUST be wrapped in a container with `overflow-x: auto`. The wrapper MUST expose a horizontal scroll affordance without breaking the page layout. The wrapper MUST be implemented as `.jcs-table-scroll` for reuse across pages.

#### Scenario: Trades table on 360 px viewport

- GIVEN a viewport of 360 px wide and the trades list page with 8 columns
- WHEN the page renders
- THEN the table MUST be wrapped in `.jcs-table-scroll`
- AND horizontal scrolling MUST be possible
- AND the page MUST NOT introduce a horizontal scroll on the body

#### Scenario: Admin subscriptions list on 360 px viewport

- GIVEN a viewport of 360 px wide and the admin subscriptions list with 6 columns
- WHEN the page renders
- THEN the table MUST be wrapped in `.jcs-table-scroll`
- AND each row MUST remain readable when scrolled horizontally

### Requirement: Sticky header on scroll

When the table container is taller than the viewport and the user scrolls vertically, the `<thead>` MUST remain visible at the top of the container. This is achieved via `position: sticky; top: 0` on the `<th>` cells inside the scroll wrapper.

#### Scenario: Long table scroll

- GIVEN a table with 100 rows
- WHEN the user scrolls vertically through the table
- THEN the header row MUST remain visible at the top of the scroll wrapper
- AND the header MUST have a visual separator (border-bottom) so the user always sees the column labels

### Requirement: Min-width per column for legibility

The table MUST declare a `min-width` on the `<table>` element (e.g. `760px` for the trades table) so columns don't collapse below their legible size. The `<th>` cells MAY declare individual `min-width` values for narrow columns.

#### Scenario: Trades table column density

- GIVEN the trades table has 8 columns (Fecha, Símbolo, Sentido, Estado, Vol, Entrada, Salida, P&L)
- WHEN the table renders in a 360 px viewport
- THEN each column MUST retain its readable width
- AND no column MUST be narrower than 80 px

### Requirement: Card-stack utility for low-density layouts

A utility class `.jcs-cards-stack-mobile` MUST be available for surfaces that benefit from stacking vertically on mobile (e.g. a grid of feature cards). The utility MUST collapse any grid to single column at < 768 px with `gap: var(--sp-4)`.

#### Scenario: Card grid on mobile

- GIVEN a grid with `grid-template-columns: repeat(3, 1fr)` and the `.jcs-cards-stack-mobile` class
- WHEN the viewport is < 768 px
- THEN the grid MUST collapse to `1fr`
- AND the gap MUST be `var(--sp-4)`

### Requirement: Decision matrix — tables vs card-stack

The implementation MUST follow this matrix when rendering data lists:

- **Tables with > 4 columns** → horizontal scroll wrapper (`.jcs-table-scroll`). Reason: density preservation.
- **Tables with 3–4 columns** → horizontal scroll wrapper (`.jcs-table-scroll`). Reason: density preservation; card-stack loses comparability.
- **Loose card grids** (feature cards, plan cards, KPI cards) → `.jcs-cards-stack-mobile` utility.

The card-stack transformation of data tables is **NOT** in scope for this change; it may be revisited in a future wave if user research supports it.

#### Scenario: Trades table (8 columns)

- GIVEN the trades list table has 8 columns
- WHEN the responsive strategy is applied
- THEN horizontal scroll MUST be used
- AND card-stack MUST NOT be applied

#### Scenario: Pricing grid (3 plan cards)

- GIVEN the pricing grid has 3 plan cards
- WHEN the responsive strategy is applied
- THEN `.jcs-cards-stack-mobile` MUST be applied
- AND the grid MUST collapse to 1 column on mobile

### Requirement: Public-pages already responsive — no regression

The pricing page and FAQ page already declare `< 768 px` rules. The refresh MUST NOT regress those rules. The implementation MUST verify:

- `pricing-page.ts` has `@media (max-width: 768px) { .grid { grid-template-columns: 1fr; } }` (already present).
- `faq-page.ts` uses `<details>` with stack layout (already responsive).

#### Scenario: Pricing on mobile

- GIVEN the pricing page
- WHEN the viewport is < 768 px
- THEN the plan cards MUST stack vertically
- AND the layout MUST remain readable

#### Scenario: FAQ on mobile

- GIVEN the FAQ page
- WHEN the viewport is < 768 px
- THEN the questions MUST stack vertically
- AND the `<details>` accordions MUST remain interactive
