import { CanMatchFn, Router } from '@angular/router';
import { inject } from '@angular/core';
import { AuthState } from '../state/auth.state';

export const authGuard: CanMatchFn = () => {
  const auth = inject(AuthState);
  const router = inject(Router);
  return auth.isAuthenticated() ? true : router.createUrlTree(['/auth/login']);
};
