import { Routes } from '@angular/router';

export const QUOTES_ROUTES: Routes = [
  {
    path: '',
    loadComponent: () =>
      import('./quotes-page').then(m => m.QuotesPage),
  },
];
