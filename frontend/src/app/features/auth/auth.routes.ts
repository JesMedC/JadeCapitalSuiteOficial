import { Routes } from '@angular/router';
import { forcedChangeGuard } from '@core/guards/forced-change.guard';

export const authRoutes: Routes = [
  { path: 'login', loadComponent: () => import('./login/login.page').then(m => m.default) },
  { path: 'register', loadComponent: () => import('./register/register.page').then(m => m.default) },
  { path: 'forgot-password', loadComponent: () => import('./recovery/forgot-password.page').then(m => m.ForgotPasswordPage) },
  {
    path: 'forced-change',
    loadComponent: () => import('./recovery/forced-change.page').then(m => m.ForcedChangePage),
    canMatch: [forcedChangeGuard],
  },
  { path: '', pathMatch: 'full', redirectTo: 'login' },
];
