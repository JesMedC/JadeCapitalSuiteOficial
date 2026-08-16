# Public Mobile Specification

## Purpose

Make the public portal (landing page, pricing, FAQ) usable on mobile viewports (< 768 px). The public surface has no authenticated user, so the pattern differs from the authenticated shells: a hamburger menu with a slide-in drawer replaces the inline nav links. Pricing and FAQ already have basic responsive rules — those must not regress.

## Requirements

### Requirement: Landing topbar collapses to hamburger on mobile

The landing-page topbar MUST hide the inline nav links at < 768 px and show a hamburger button instead. Tapping the hamburger MUST open a slide-in drawer that exposes the same nav links plus the authentication CTAs (Login / Registrarse / Dashboard).

#### Scenario: iPhone viewport on landing

- GIVEN a viewport of 390 px wide
- WHEN the landing page renders
- THEN the inline nav links (Home, Nosotros, Planes, Contacto) MUST be hidden
- AND a hamburger button MUST be visible
- AND the authentication CTAs (Login button) MUST remain visible (or accessible via the drawer)

#### Scenario: Tablet viewport on landing

- GIVEN a viewport of 768 px wide
- WHEN the landing page renders
- THEN the inline nav links MUST be visible
- AND the hamburger button MUST be hidden

### Requirement: Hamburger drawer animation

The drawer MUST slide in from the right (or left — implementation choice) over 200–300 ms. The drawer MUST have a backdrop overlay that closes the drawer when tapped. The drawer MUST be dismissable via the close button or the Escape key.

#### Scenario: Opening the drawer

- GIVEN the drawer is closed
- WHEN the user taps the hamburger button
- THEN the drawer MUST slide in within 200–300 ms
- AND the backdrop MUST appear
- AND focus MUST move to the close button (or first focusable element)

#### Scenario: Closing the drawer

- GIVEN the drawer is open
- WHEN the user taps the backdrop or close button
- THEN the drawer MUST close within 200–300 ms
- AND focus MUST return to the hamburger button

#### Scenario: Escape key closes the drawer

- GIVEN the drawer is open
- WHEN the user presses `Escape`
- THEN the drawer MUST close
- AND focus MUST return to the hamburger button

### Requirement: Pricing grid collapses to 1 column on mobile

The pricing-page grid (`grid-template-columns: repeat(3, 1fr)`) MUST collapse to `1fr` at < 768 px. The collapse MUST use `--bp-tablet` token. The plan cards MUST remain readable and the CTA buttons MUST have tap targets ≥ 44 px.

#### Scenario: Pricing on 390 px viewport

- GIVEN a viewport of 390 px wide
- WHEN the pricing page renders
- THEN the 3 plan cards MUST stack vertically
- AND each CTA MUST have a tap target ≥ 44 px

#### Scenario: Pricing on 1024 px viewport

- GIVEN a viewport of 1024 px wide
- WHEN the pricing page renders
- THEN the 3 plan cards MUST display in a single row
- AND the grid gap MUST match the desktop token

### Requirement: FAQ accordion works on mobile

The FAQ page uses native `<details>` elements. The accordions MUST stack vertically on all viewports (no change needed). The toggle MUST have a tap target ≥ 44 px. The accordion animation (open/close) MUST remain smooth on mobile.

#### Scenario: FAQ on 390 px viewport

- GIVEN a viewport of 390 px wide
- WHEN the FAQ page renders
- THEN the questions MUST stack vertically
- AND tapping a question MUST toggle the answer
- AND the toggle control MUST have a tap target ≥ 44 px

### Requirement: No bottom-tab bar on public

The public portal MUST NOT include a bottom tab bar. Public users have no profile, no auth-required sections, and the marketing surface is content-driven. The hamburger drawer is the only navigation mechanic.

#### Scenario: Public landing without auth

- GIVEN a non-authenticated user on the landing page
- WHEN the page renders on mobile
- THEN no bottom tab bar MUST be present
- AND the hamburger drawer MUST be the only navigation

#### Scenario: Public landing with auth

- GIVEN an authenticated user on the landing page
- WHEN the page renders on mobile
- THEN the "Dashboard" CTA MUST be visible in the drawer
- AND no bottom tab bar MUST be present

### Requirement: Public shell does not import `<jcs-mobile-nav>`

The public components (`landing-page.ts`, `pricing-page.ts`, `faq-page.ts`) MUST NOT import `<jcs-mobile-nav>`. They are responsible for their own navigation patterns (topbar + hamburger drawer).

#### Scenario: Public component imports

- GIVEN the public shell components
- WHEN inspected
- THEN they MUST NOT import `JcsMobileNav` or `<jcs-mobile-nav>`
- AND the navigation MUST be self-contained via topbar + drawer
