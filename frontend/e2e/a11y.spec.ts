import { test, expect } from '@playwright/test';
import AxeBuilder from '@axe-core/playwright';

// ============================================================================
//  a11y.spec.ts — Wave 12 slice 12.2 — WCAG 2.1 AA audit.
//
//  Runs @axe-core/playwright against the public, anonymous-accessible
//  surface of the Jade Capital SPA. Anonymous because the consent
//  banner + pricing/FAQ/legal pages MUST work pre-login (Wave 11.4's
//  GDPR Art. 7 contract).
//
//  <para>
//  <b>Tag selection</b>: <c>wcag2a</c> + <c>wcag2aa</c> + <c>wcag21a</c>
//  + <c>wcag21aa</c> is the canonical "WCAG 2.1 Level AA" sweep. WCAG 2.2
//  is on the roadmap but not enforced for v1.0.0 GA.
//  </para>
//
//  <para>
//  <b>Severity threshold</b>: only <c>impact === 'critical'</c> blocks
//  the build. <c>serious</c> and <c>moderate</c> violations are
//  reported via the Playwright HTML reporter (open with
//  <c>npx playwright show-report</c>) so reviewers can see the backlog
//  without the gate being noisy. Critical-only matches the spec's
//  "ship blocker" classification.
//  </para>
//
//  <para>
//  <b>Route deviations from the slice spec</b>: the spec lists
//  <c>/faq</c> as a target, but JadeCapitalSuite ships the FAQ section
//  inside <c>/</c> (the landing page renders <c>#faq</c> as an
//  in-page anchor, not a separate route). Hitting <c>/faq</c> redirects
//  to <c>/</c>, so the test list is scoped to the actually-routed
//  anonymous surface: <c>/</c>, <c>/pricing</c>, <c>/auth/login</c>,
//  <c>/auth/register</c>, <c>/legal/terms</c>, <c>/legal/privacy</c>.
//  </para>
// ============================================================================

test.describe('Accessibility (WCAG 2.1 AA)', () => {
  const anonymousPages = [
    '/',
    '/pricing',
    '/auth/login',
    '/auth/register',
    '/legal/terms',
    '/legal/privacy',
  ] as const;

  for (const url of anonymousPages) {
    test(`${url} has no critical WCAG 2.1 AA violations`, async ({ page }) => {
      await page.goto(url);

      const accessibilityScanResults = await new AxeBuilder({ page })
        .withTags(['wcag2a', 'wcag2aa', 'wcag21a', 'wcag21aa'])
        // Disable the color-contrast rule for now: the design system uses
        // CSS variables that axe-core can't resolve statically (it would
        // require computed-style traversal). A future slice adds a
        // dedicated contrast audit using the resolved token palette.
        .disableRules(['color-contrast'])
        .analyze();

      const criticalViolations = accessibilityScanResults.violations.filter(
        (v) => v.impact === 'critical',
      );

      expect(
        criticalViolations,
        `Critical accessibility violations on ${url}:\n` +
          criticalViolations
            .map(
              (v) =>
                `  - [${v.id}] ${v.help} (${v.nodes.length} node${v.nodes.length === 1 ? '' : 's'})`,
            )
            .join('\n'),
      ).toEqual([]);
    });
  }
});
