import { Routes } from '@angular/router';

export const traderRoutes: Routes = [
  { path: 'dashboard', loadComponent: () => import('./dashboard/dashboard.page').then((m) => m.DashboardPage) },
  { path: 'trades', loadComponent: () => import('./trades/trades-list.page').then((m) => m.TradesListPage) },
  { path: 'calendar', loadComponent: () => import('./calendar/calendar.page').then((m) => m.CalendarPage) },
  { path: 'settings', loadComponent: () => import('./settings/settings.page').then((m) => m.SettingsPage) },
  { path: '', pathMatch: 'full', redirectTo: 'dashboard' },
];
