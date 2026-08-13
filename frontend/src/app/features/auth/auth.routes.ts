import { Routes } from '@angular/router';
import { forcedChangeGuard } from '@core/guards/forced-change.guard';

export const authRoutes: Routes = [
  { path: 'login', loadComponent: () => import('./login/login.page') },
  { path: 'register', loadComponent: () => import('./register/register.page') },
  { path: 'forgot-password', loadComponent: () => import('./recovery/forgot-password.page') },
  {
    path: 'forced-change',
    loadComponent: () => import('./recovery/forced-change.page'),
    canMatch: [forcedChangeGuard],
  },
  { path: '', pathMatch: 'full', redirectTo: 'login' },
];
