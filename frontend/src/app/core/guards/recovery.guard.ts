import { CanMatchFn, Router } from '@angular/router';
import { inject } from '@angular/core';
import { AuthState } from '../state/auth.state';

export const recoveryGuard: CanMatchFn = (route) => {
  const auth = inject(AuthState);
  const router = inject(Router);
  if (auth.passwordChangeRequired()) {
    return router.createUrlTree(['/auth/forced-change']);
  }
  return true;
};
