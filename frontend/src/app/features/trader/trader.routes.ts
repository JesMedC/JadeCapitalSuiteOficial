import { Routes } from '@angular/router';

export const traderRoutes: Routes = [
  { path: 'dashboard', loadComponent: () => import('./dashboard/dashboard.page').then((m) => m.DashboardPage) },
  { path: 'trades', loadComponent: () => import('./trades/trades-list.page').then((m) => m.TradesListPage) },
  // Slice 1d.2 — trade detail (review form hosted here).
  { path: 'trades/:tradeId', loadComponent: () => import('./trades/trade-detail.page').then((m) => m.TradeDetailPage) },
  // Slice 2a.2 — daily journal.
  { path: 'journal', loadChildren: () => import('./journal/journal.routes').then((m) => m.JOURNAL_ROUTES) },
  // Slice 2b.2 — behavioral patterns (Patrones conductuales).
  { path: 'patterns', loadChildren: () => import('./patterns/patterns.routes').then((m) => m.PATTERNS_ROUTES) },
  { path: 'calendar', loadComponent: () => import('./calendar/calendar.page').then((m) => m.CalendarPage) },
  { path: 'settings', loadComponent: () => import('./settings/settings.page').then((m) => m.SettingsPage) },
  { path: '', pathMatch: 'full', redirectTo: 'dashboard' },
];
