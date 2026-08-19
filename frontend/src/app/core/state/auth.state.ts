import { Injectable, computed, inject, signal } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable, catchError, finalize, firstValueFrom, map, shareReplay, tap, throwError } from 'rxjs';

const ACCESS_KEY = 'jcs.access';
const REFRESH_KEY = 'jcs.refresh';
const USER_KEY = 'jcs.user';

export interface User {
  id: string;
  email: string;
  displayName: string;
  role: string;
}

interface AuthResponse {
  accessToken: string;
  accessTokenExpiresAt: string;
  refreshToken: string;
  refreshTokenExpiresAt: string;
  userId: string;
  email: string;
  displayName: string;
  role: string;
}

@Injectable({ providedIn: 'root' })
export class AuthState {
  private readonly http = inject(HttpClient);

  private readonly _user = signal<User | null>(this.loadUser());
  private readonly _accessToken = signal<string | null>(sessionStorage.getItem(ACCESS_KEY));
  private readonly _passwordChangeRequired = signal<boolean>(false);
  private readonly _recoveryGrant = signal<string | null>(null);
  private readonly _generation = signal<number>(0);

  readonly user = this._user.asReadonly();
  readonly isAuthenticated = computed(() => this._accessToken() !== null && this._user() !== null);
  readonly isAdmin = computed(() => this._user()?.role === 'Admin');
  readonly passwordChangeRequired = this._passwordChangeRequired.asReadonly();
  readonly recoveryGrant = this._recoveryGrant.asReadonly();
  readonly generation = this._generation.asReadonly();

  markPasswordChangeRequired(grantJti: string, generation: number): void {
    this._recoveryGrant.set(grantJti);
    this._generation.set(generation);
    this._passwordChangeRequired.set(true);
  }

  clearPasswordChangeRequired(): void {
    this._recoveryGrant.set(null);
    this._generation.set(0);
    this._passwordChangeRequired.set(false);
  }

  private refreshInProgress$: Observable<boolean> | null = null;

  async register(
    email: string,
    displayName: string,
    password: string,
    acceptTerms: boolean,
    acceptPrivacy: boolean,
  ): Promise<void> {
    const consentIp = await this.detectClientIp();
    await firstValueFrom(
      this.http.post('/api/auth/register', {
        email,
        displayName,
        password,
        acceptTerms,
        acceptPrivacy,
        consentIp,
      }),
    );
    await this.login(email, password);
  }

  private async detectClientIp(): Promise<string> {
    try {
      const resp = await firstValueFrom(
        this.http.get<{ ip?: string }>('/api/util/client-ip'),
      );
      return resp.ip ?? '0.0.0.0';
    } catch {
      return '0.0.0.0';
    }
  }

  async login(email: string, password: string): Promise<void> {
    const resp = await firstValueFrom(this.http.post<AuthResponse>('/api/auth/login', { email, password }));
    this.persist(resp);
  }

  async refresh(): Promise<void> {
    await firstValueFrom(this.refresh$());
  }

  refresh$(): Observable<boolean> {
    if (this.refreshInProgress$) {
      return this.refreshInProgress$;
    }

    const refreshToken = localStorage.getItem(REFRESH_KEY);
    if (!refreshToken) {
      return throwError(() => new Error('No refresh token'));
    }

    this.refreshInProgress$ = this.http
      .post<AuthResponse>('/api/auth/refresh', { refreshToken })
      .pipe(
        tap((resp) => this.persist(resp)),
        map(() => true),
        catchError((err) => {
          this.logout();
          return throwError(() => err);
        }),
        finalize(() => (this.refreshInProgress$ = null)),
        shareReplay({ bufferSize: 1, refCount: false }),
      );

    return this.refreshInProgress$;
  }

  logout(): void {
    sessionStorage.removeItem(ACCESS_KEY);
    localStorage.removeItem(REFRESH_KEY);
    localStorage.removeItem(USER_KEY);
    this._accessToken.set(null);
    this._user.set(null);
  }

  getAccessToken(): string | null {
    return this._accessToken();
  }

  private persist(resp: AuthResponse): void {
    sessionStorage.setItem(ACCESS_KEY, resp.accessToken);
    localStorage.setItem(REFRESH_KEY, resp.refreshToken);
    const user: User = {
      id: resp.userId,
      email: resp.email,
      displayName: resp.displayName,
      role: resp.role,
    };
    localStorage.setItem(USER_KEY, JSON.stringify(user));
    this._accessToken.set(resp.accessToken);
    this._user.set(user);
  }

  private loadUser(): User | null {
    const raw = localStorage.getItem(USER_KEY);
    if (!raw) return null;
    try {
      const parsed = JSON.parse(raw) as User & { tier?: string };
      delete parsed.tier;
      return parsed;
    } catch {
      return null;
    }
  }
}
