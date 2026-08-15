import { TestBed } from '@angular/core/testing';
import { Router, UrlTree } from '@angular/router';
import { provideHttpClient } from '@angular/common/http';
import { AuthState } from '../../state/auth.state';
import { forcedChangeGuard } from '../forced-change.guard';

describe('forcedChangeGuard', () => {
  const router: { createUrlTree: jest.Mock } = {
    createUrlTree: jest.fn((segments: string[]) => {
      const joined = segments.join('/');
      return (joined.startsWith('/') ? joined : `/${joined}`) as unknown as UrlTree;
    }),
  };

  const configureAuth = (opts: { authenticated: boolean; passwordChangeRequired: boolean }): void => {
    sessionStorage.clear();
    localStorage.clear();
    if (opts.authenticated) {
      sessionStorage.setItem('jcs.access', 'fake-access');
      localStorage.setItem(
        'jcs.user',
        JSON.stringify({ id: 'u-1', email: 'a@b.com', displayName: 'User', role: 'Trader' }),
      );
    }
    TestBed.resetTestingModule();
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), AuthState, { provide: Router, useValue: router }],
    });
    const auth = TestBed.inject(AuthState);
    router.createUrlTree.mockClear();
    if (opts.passwordChangeRequired) auth.markPasswordChangeRequired('grant-jti-abc', 1);
  };

  const runGuard = () =>
    TestBed.runInInjectionContext(() => forcedChangeGuard({} as never, [] as never));

  it('AllowsOnlyChangeRoute — allows the change route when authenticated and password change is required', () => {
    configureAuth({ authenticated: true, passwordChangeRequired: true });

    const result = runGuard();

    expect(result).toBe(true);
    expect(router.createUrlTree).not.toHaveBeenCalled();
  });

  it('redirects to /auth/login when not authenticated', () => {
    configureAuth({ authenticated: false, passwordChangeRequired: false });

    const result = runGuard();

    expect(result).toBe('/auth/login');
    expect(router.createUrlTree).toHaveBeenCalledWith(['/auth/login']);
  });

  it('redirects to /app/dashboard when authenticated but no change required', () => {
    configureAuth({ authenticated: true, passwordChangeRequired: false });

    const result = runGuard();

    expect(result).toBe('/app/dashboard');
    expect(router.createUrlTree).toHaveBeenCalledWith(['/app/dashboard']);
  });
});
