import { Routes } from '@angular/router';

// ============================================================================
//  Settings routes — Wave 11 slice 11.2b.
//
//  Lazy-loaded child routes for the trader-shell's `/settings` path.
//  Mounted under the same path as `trader/settings/settings.page.ts` so the
//  page is reachable without disturbing the existing settings tabs.
//
//  Auth: the trader shell itself is guarded by `authGuard` (see
//  app.routes.ts → /app). The account-deletion page is mounted under that
//  shell, so it inherits the auth requirement automatically. No additional
//  guard is needed here.
// ============================================================================

export const SETTINGS_ROUTES: Routes = [
  {
    path: 'delete-account',
    loadComponent: () =>
      import('./account-deletion/account-deletion.page').then((m) => m.AccountDeletionPage),
    title: 'Eliminar mi cuenta · JadeCapitalSuite',
  },
];
