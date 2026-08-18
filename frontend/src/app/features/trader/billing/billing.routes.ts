import { Routes } from '@angular/router';

// ============================================================================
//  Billing routes — slice 6b.2 frontend.
//
//  Lazy-loaded child routes for the trader-shell's `/billing` path.
//  Only one page (the Billing Portal); sub-routes (e.g. `/billing/methods`
//  in a future slice) can be appended here without touching the trader
//  routes file.
//
//  Auth: the trader shell itself is guarded by `authGuard` (see
//  app.routes.ts → /app). The billing portal is mounted under that shell,
//  so it inherits the auth requirement automatically. No additional guard
//  is needed here.
// ============================================================================

export const BILLING_ROUTES: Routes = [
  {
    path: '',
    loadComponent: () => import('./billing-portal-page').then((m) => m.BillingPortalPage),
  },
];
