import { inject } from '@angular/core';
import { CanMatchFn, Router } from '@angular/router';
import { AuthState } from '@core/state/auth.state';

/**
 * CanMatch guard for /admin/* routes. Requires authentication AND role=Admin.
 * Non-admins are redirected to /app/dashboard; unauthenticated users fall
 * through to the auth guard (which redirects to /auth/login).
 */
export const adminGuard: CanMatchFn = () => {
  const auth = inject(AuthState);
  const router = inject(Router);
  if (!auth.isAuthenticated()) return router.parseUrl('/auth/login');
  if (!auth.isAdmin()) return router.parseUrl('/app/dashboard');
  return true;
};
