import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { AuthState } from '../../state/auth.state';

describe('AuthState', () => {
  let state: AuthState;

  beforeEach(() => {
    sessionStorage.clear();
    localStorage.clear();
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), AuthState],
    });
    state = TestBed.inject(AuthState);
  });

  it('AuthState_MarksPasswordChangeRequired sets the recovery grant, generation, and flag', () => {
    expect(state.passwordChangeRequired()).toBe(false);
    expect(state.recoveryGrant()).toBeNull();
    expect(state.generation()).toBe(0);

    state.markPasswordChangeRequired('grant-jti-abc', 7);

    expect(state.passwordChangeRequired()).toBe(true);
    expect(state.recoveryGrant()).toBe('grant-jti-abc');
    expect(state.generation()).toBe(7);

    state.clearPasswordChangeRequired();
    expect(state.passwordChangeRequired()).toBe(false);
    expect(state.recoveryGrant()).toBeNull();
    expect(state.generation()).toBe(0);
  });
});
