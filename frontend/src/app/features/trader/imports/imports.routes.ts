import { Routes } from '@angular/router';

export const IMPORTS_ROUTES: Routes = [
  {
    path: '',
    loadComponent: () =>
      import('./imports-page').then(m => m.ImportsPage),
  },
];