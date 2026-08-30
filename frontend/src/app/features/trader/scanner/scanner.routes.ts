import { Routes } from '@angular/router';

export const SCANNER_ROUTES: Routes = [
  {
    path: '',
    loadComponent: () =>
      import('./scanner-page').then(m => m.ScannerPage),
  },
];
