import { Routes } from '@angular/router';

export const adminRoutes: Routes = [
  {
    path: '',
    loadComponent: () => import('./admin-shell').then((m) => m.AdminShell),
    children: [
      { path: '', pathMatch: 'full', redirectTo: 'subscriptions' },
      {
        path: 'subscriptions',
        loadComponent: () =>
          import('./subscriptions/admin-list.page').then((m) => m.AdminSubscriptionsListPage),
      },
      {
        path: 'subscriptions/:id',
        loadComponent: () =>
          import('./subscriptions/admin-detail.page').then((m) => m.AdminSubscriptionDetailPage),
      },
    ],
  },
];
