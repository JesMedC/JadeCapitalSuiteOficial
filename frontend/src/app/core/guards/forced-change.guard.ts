import { CanMatchFn, Router } from '@angular/router';
import { inject } from '@angular/core';
import { AuthState } from '../state/auth.state';

export const forcedChangeGuard: CanMatchFn = (route) => {
  const auth = inject(AuthState);
  const router = inject(Router);
  if (!auth.isAuthenticated()) {
    return router.createUrlTree(['/auth/login']);
  }
  if (!auth.passwordChangeRequired()) {
    return router.createUrlTree(['/app/dashboard']);
  }
  return true;
};
