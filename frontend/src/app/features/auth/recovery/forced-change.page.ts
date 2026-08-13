import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { HttpClient } from '@angular/common/http';
import { Router, RouterLink } from '@angular/router';
import { AuthState } from '@core/state/auth.state';

interface ChangePasswordResponse {
  accessToken: string;
  accessTokenExpiresAt: string;
  refreshToken: string;
  refreshTokenExpiresAt: string;
  userId: string;
}

@Component({
  selector: 'jcs-forced-change',
  standalone: true,
  imports: [ReactiveFormsModule, RouterLink],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="jcs-container recovery-shell">
      <div class="jcs-card jcs-card--glow recovery-card">
        <h1 class="recovery-title">Establecé tu nueva contraseña</h1>
        <p class="recovery-sub">Por seguridad, cambiá la contraseña temporal antes de continuar.</p>
        @if (!hasGrant()) {
          <p class="jcs-error" role="alert">Sesión inválida. <a routerLink="/auth/login">Volver a iniciar sesión</a></p>
        } @else {
          <form [formGroup]="form" (ngSubmit)="submit()" novalidate>
            <label class="jcs-label" for="newPassword">Nueva contraseña</label>
            <input id="newPassword" class="jcs-input" type="password" formControlName="newPassword"
                   autocomplete="new-password" placeholder="Mínimo 12 caracteres" aria-describedby="status"
                   [class.jcs-input--error]="form.controls.newPassword.touched && form.controls.newPassword.invalid" />
            @if (form.controls.newPassword.touched && form.controls.newPassword.invalid) {
              <small class="jcs-error">Mínimo 12 caracteres con al menos un dígito y un símbolo.</small>
            }
            <button class="jcs-btn jcs-btn--primary" type="submit"
                    [disabled]="loading() || form.invalid" aria-busy="{{ loading() }}">
              {{ loading() ? 'Guardando…' : 'Cambiar contraseña' }}
            </button>
          </form>
        }
        <p id="status" class="recovery-status" role="alert" aria-live="assertive">{{ status() }}</p>
      </div>
    </div>
  `,
  styles: [`
    .recovery-shell { max-width: 480px; padding: 64px 24px; }
    .recovery-card { padding: 32px; }
    .recovery-title { font-size: 1.5rem; margin: 0 0 8px; }
    .recovery-sub { color: var(--muted); margin: 0 0 24px; font-size: 0.9rem; }
    form { display: flex; flex-direction: column; gap: 12px; }
    .recovery-status { min-height: 1.25em; margin: 16px 0 0; font-size: 0.9rem; }
    .recovery-status:not(:empty) { color: #a92323; }
    .jcs-error { color: #a92323; font-size: 0.85rem; }
    @media (max-width: 360px) {
      .recovery-shell { padding: 32px 12px; }
      .recovery-card { padding: 20px 16px; }
    }
  `],
})
export class ForcedChangePage {
  private readonly fb = inject(FormBuilder);
  private readonly http = inject(HttpClient);
  private readonly router = inject(Router);
  private readonly auth = inject(AuthState);

  readonly hasGrant = computed(() => this.auth.recoveryGrant() !== null);

  readonly form = this.fb.nonNullable.group({
    newPassword: ['', [Validators.required, Validators.minLength(12), this.complexityValidator]],
  });

  readonly loading = signal(false);
  readonly status = signal('');

  private complexityValidator(control: { value: string }): { complexity: true } | null {
    const v = control.value ?? '';
    if (!v) return null;
    const ok = /[A-Za-z]/.test(v) && /\d/.test(v) && /[^A-Za-z0-9]/.test(v);
    return ok ? null : { complexity: true };
  }

  submit(): void {
    if (this.form.invalid || this.loading()) return;
    const grantJti = this.auth.recoveryGrant();
    const generation = this.auth.generation();
    if (!grantJti) {
      this.status.set('Sesión inválida. Volvé a iniciar sesión.');
      return;
    }
    const newPassword = this.form.controls.newPassword.value;
    this.loading.set(true);
    this.status.set('');
    this.http
      .post<ChangePasswordResponse>('/api/auth/change-password', {
        grantJti,
        expectedSessionVersion: generation,
        newPassword,
      })
      .subscribe({
        next: () => {
          this.auth.clearPasswordChangeRequired();
          this.loading.set(false);
          void this.router.navigateByUrl('/app/dashboard');
        },
        error: (err) => {
          this.loading.set(false);
          this.status.set(
            err?.status === 409
              ? 'La contraseña nueva debe ser distinta de las últimas cinco.'
              : 'No pudimos cambiar la contraseña. Intentá nuevamente.'
          );
        },
      });
  }
}
