import { Routes } from '@angular/router';

// ============================================================================
//  Patterns routes — slice 2b.2 frontend.
//
//  Lazy-loaded child routes for /patterns inside the trader shell.
//  Today: a single page that renders the behavioral analysis for the
//  active period selector. Wave 3 may split per-rule drilldowns
//  (e.g. /patterns/revenge).
// ============================================================================

export const PATTERNS_ROUTES: Routes = [
  {
    path: '',
    loadComponent: () => import('./patterns-page').then((m) => m.PatternsPage),
  },
];
