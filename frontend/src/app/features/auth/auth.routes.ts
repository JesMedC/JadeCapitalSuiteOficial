import { Routes } from '@angular/router';

export const authRoutes: Routes = [
  { path: 'login', loadComponent: () => import('./login/login.page') },
  { path: 'register', loadComponent: () => import('./register/register.page') },
  { path: '', pathMatch: 'full', redirectTo: 'login' },
];
