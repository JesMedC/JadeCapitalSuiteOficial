import { Routes } from '@angular/router';

export const PLANNER_ROUTES: Routes = [
  {
    path: '',
    loadComponent: () =>
      import('./planner-page').then(m => m.PlannerPage),
  },
];
