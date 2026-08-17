import { Routes } from '@angular/router';

// ============================================================================
//  Strategies routes — slice 3a frontend.
//
//  Lazy-loaded child routes for /strategies inside the trader shell.
//  Only one page (the strategies list); sub-routes (/:id) reserved for
//  future individual pages (slice 4+).
// ============================================================================

export const STRATEGIES_ROUTES: Routes = [
  {
    path: '',
    loadComponent: () => import('./strategies-page').then((m) => m.StrategiesPage),
  },
];