import { TestBed } from '@angular/core/testing';
import { Router, UrlTree } from '@angular/router';
import { provideHttpClient } from '@angular/common/http';
import { AuthState } from '../../../core/state/auth.state';
import { adminGuard } from '../../../core/guards/admin.guard';

describe('adminGuard', () => {
  const router: { parseUrl: jest.Mock } = {
    parseUrl: jest.fn((url: string) => url as unknown as UrlTree),
  };

  const configureAuth = (role: string | null): void => {
    sessionStorage.clear();
    localStorage.clear();
    if (role) {
      sessionStorage.setItem('jcs.access', 'fake-access');
      localStorage.setItem(
        'jcs.user',
        JSON.stringify({ id: 'u-1', email: 'a@b.com', displayName: 'User', role }),
      );
    }
    TestBed.resetTestingModule();
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), AuthState, { provide: Router, useValue: router }],
    });
    router.parseUrl.mockClear();
  };

  const runGuard = () =>
    TestBed.runInInjectionContext(() => adminGuard({} as never, [] as never));

  it('BlocksNonAdmin_AndForcedChange — redirects unauthenticated user to /auth/login', () => {
    configureAuth(null);
    TestBed.inject(AuthState);

    const result = runGuard();

    expect(router.parseUrl).toHaveBeenCalledWith('/auth/login');
    expect(result).toBe('/auth/login');
  });

  it('BlocksNonAdmin_AndForcedChange — redirects non-admin authenticated user to /app/dashboard', () => {
    configureAuth('Trader');
    TestBed.inject(AuthState);

    const result = runGuard();

    expect(router.parseUrl).toHaveBeenCalledWith('/app/dashboard');
    expect(result).toBe('/app/dashboard');
  });

  it('BlocksNonAdmin_AndForcedChange — allows Admin role through (recovery.guard owns the forced-change redirect)', () => {
    configureAuth('Admin');
    TestBed.inject(AuthState);

    const result = runGuard();

    expect(result).toBe(true);
    expect(router.parseUrl).not.toHaveBeenCalled();
  });
});
