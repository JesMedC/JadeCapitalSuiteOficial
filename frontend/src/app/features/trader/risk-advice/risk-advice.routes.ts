import { Routes } from '@angular/router';

export const RISK_ADVICE_ROUTES: Routes = [
  {
    path: '',
    loadComponent: () =>
      import('./risk-advice-panel').then(m => m.RiskAdvicePanel),
  },
];
