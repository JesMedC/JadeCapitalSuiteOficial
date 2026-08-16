import { Routes } from '@angular/router';

// ============================================================================
//  Journal routes — slice 2a.2 frontend.
//
//  Lazy-loaded child routes for /journal inside the trader shell. Only one
//  page (today's entry); the historical list lives behind /journal?from=&to=
//  when slice 2a.3 lands. Kept as its own file to match the trader.routes
//  pattern (loadChildren → module-style routes).
// ============================================================================

export const JOURNAL_ROUTES: Routes = [
  {
    path: '',
    loadComponent: () => import('./journal-page').then((m) => m.JournalPage),
  },
];
