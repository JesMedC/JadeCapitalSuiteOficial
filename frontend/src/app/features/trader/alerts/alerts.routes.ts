import { Routes } from '@angular/router';

// ============================================================================
//  Alerts routes — slice 3b frontend.
//
//  Lazy-loaded child routes for /alerts inside the trader shell. Only the
//  list page is shipped today; future /alerts/:id (detail view) is a
//  slice 4+ concern.
// ============================================================================

export const ALERTS_ROUTES: Routes = [
  {
    path: '',
    loadComponent: () => import('./alerts-page').then((m) => m.AlertsPage),
  },
];