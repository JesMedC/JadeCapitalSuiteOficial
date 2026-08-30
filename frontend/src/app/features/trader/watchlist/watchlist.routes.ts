import { Routes } from '@angular/router';

export const WATCHLIST_ROUTES: Routes = [
  {
    path: '',
    loadComponent: () => import('./watchlist-page').then((m) => m.WatchlistPage),
  },
];