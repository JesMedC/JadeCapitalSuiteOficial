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
  // Slice 3a.2 — trader strategies (named setups + analytics).
  { path: 'strategies', loadChildren: () => import('./strategies/strategies.routes').then((m) => m.STRATEGIES_ROUTES) },
  // Slice 3b.2 — alerts (BackgroundService + ack flow).
  { path: 'alerts', loadChildren: () => import('./alerts/alerts.routes').then((m) => m.ALERTS_ROUTES) },
  // Slice 3c.2 — weekly planner (planned vs actual trading sessions).
  { path: 'planner', loadChildren: () => import('./planner/planner.routes').then((m) => m.PLANNER_ROUTES) },
  // Slice 4a — trader scanner (filter CRUD + run against instrument universe).
  { path: 'scanner', loadChildren: () => import('./scanner/scanner.routes').then((m) => m.SCANNER_ROUTES) },
  // Slice 4b — market data quotes (live HTTP + cache-backed).
  { path: 'quotes', loadChildren: () => import('./quotes/quotes.routes').then((m) => m.QUOTES_ROUTES) },
  // Slice 4c — realtime watchlist (SignalR-backed live prices).
  { path: 'watchlist', loadChildren: () => import('./watchlist/watchlist.routes').then((m) => m.WATCHLIST_ROUTES) },
  { path: 'calendar', loadComponent: () => import('./calendar/calendar.page').then((m) => m.CalendarPage) },
  { path: 'settings', loadComponent: () => import('./settings/settings.page').then((m) => m.SettingsPage) },
  { path: '', pathMatch: 'full', redirectTo: 'dashboard' },
];
