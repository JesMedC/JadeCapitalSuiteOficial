import { Routes } from '@angular/router';
import { authGuard } from './core/guards/auth.guard';

export const routes: Routes = [
  {
    path: '',
    loadComponent: () => import('./features/public/landing/landing-page').then((m) => m.LandingPage),
    pathMatch: 'full',
  },
  {
    path: 'pricing',
    loadComponent: () => import('./features/public/pricing/pricing-page').then((m) => m.PricingPage),
  },
  {
    path: 'auth',
    loadChildren: () => import('./features/auth/auth.routes').then((m) => m.authRoutes),
  },
  {
    path: 'app',
    canMatch: [authGuard],
    loadComponent: () =>
      import('./features/trader/trader-shell').then((m) => m.TraderShell),
    loadChildren: () => import('./features/trader/trader.routes').then((m) => m.traderRoutes),
  },
  { path: '**', redirectTo: '' },
];
