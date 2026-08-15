import { TestBed } from '@angular/core/testing';
import { Router, UrlTree } from '@angular/router';
import { provideHttpClient } from '@angular/common/http';
import { AuthState } from '../../state/auth.state';
import { recoveryGuard } from '../recovery.guard';

describe('recoveryGuard', () => {
  let auth: AuthState;
  const router: { createUrlTree: jest.Mock } = {
    createUrlTree: jest.fn((segments: string[]) => {
      const joined = segments.join('/');
      return (joined.startsWith('/') ? joined : `/${joined}`) as unknown as UrlTree;
    }),
  };

  beforeEach(() => {
    sessionStorage.clear();
    localStorage.clear();
    router.createUrlTree.mockClear();
    TestBed.resetTestingModule();
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), AuthState, { provide: Router, useValue: router }],
    });
    auth = TestBed.inject(AuthState);
  });

  const runGuard = () =>
    TestBed.runInInjectionContext(() => recoveryGuard({} as never, [] as never));

  it('RedirectsWhenNoChangeRequired — redirects to /auth/forced-change when password change is required', () => {
    auth.markPasswordChangeRequired('grant-jti-abc', 3);

    const result = runGuard();

    expect(result).toBe('/auth/forced-change');
    expect(router.createUrlTree).toHaveBeenCalledWith(['/auth/forced-change']);
  });

  it('allows the recovery route when no password change is required', () => {
    const result = runGuard();

    expect(result).toBe(true);
    expect(router.createUrlTree).not.toHaveBeenCalled();
  });
});
