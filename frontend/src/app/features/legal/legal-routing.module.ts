import { Routes } from '@angular/router';

// ============================================================================
//  Legal routes — Wave 11 slice 11.4.
//
//  Lazy-loaded from app.routes.ts. Both routes are anonymous (the ToS
//  + Privacy Policy must be reachable from unauthenticated registration
//  flows) and resolve to standalone components that render placeholder
//  copy with a "DO NOT DEPLOY" banner. The canonical Markdown files
//  live in `frontend/src/assets/legal/` for reference; once legal
//  counsel signs off, the components get replaced with a server-rendered
//  Markdown view (wave 12+ work).
//
//  <para>
//  Public, anonymous pages by design: the registration form links here
//  from the consent checkboxes, and a paying customer who needs the
//  canonical copy can reach the document without logging in.
//  </para>
//
//  <para>
//  <b>Why a "module" file with a single Routes export</b>: matches the
//  precedent set by <c>account-deletion.routes.ts</c> (slice 11.2b) so
//  every feature owns its route config in <c>*.routes.ts</c>, loaded via
//  <c>loadChildren</c> from <c>app.routes.ts</c>. The "module" file
//  naming is a holdover from the NgModule era; standalone routes work
//  identically once imported by <c>loadChildren</c>.
//  </para>
// ============================================================================

export const LEGAL_ROUTES: Routes = [
  {
    path: 'terms',
    loadComponent: () =>
      import('./terms-of-service.page').then((m) => m.TermsOfServicePage),
    title: 'Términos de servicio · JadeCapitalSuite',
  },
  {
    path: 'privacy',
    loadComponent: () =>
      import('./privacy-policy.page').then((m) => m.PrivacyPolicyPage),
    title: 'Política de privacidad · JadeCapitalSuite',
  },
];
