import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { AuthState } from '@core/state/auth.state';

@Component({
  selector: 'jcs-login',
  standalone: true,
  imports: [ReactiveFormsModule, RouterLink],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="shell">
      <div class="jcs-card box">
        <h2>Iniciar sesion</h2>
        <form [formGroup]="form" (ngSubmit)="submit()" class="jcs-stack">
          <label>Email
            <input class="jcs-input" type="email" formControlName="email" autocomplete="email">
          </label>
          <label>Contrasena
            <input class="jcs-input" type="password" formControlName="password" autocomplete="current-password">
          </label>
          @if (error()) { <div class="err">{{ error() }}</div> }
          <button class="jcs-btn jcs-btn--primary" type="submit" [disabled]="form.invalid || loading()">
            {{ loading() ? 'Entrando...' : 'Entrar' }}
          </button>
        </form>
        <p class="jcs-muted">No tienes cuenta? <a routerLink="/auth/register">Crear cuenta</a></p>
      </div>
    </div>
  `,
  styles: [`
    .shell { display: flex; align-items: center; justify-content: center; min-height: 100vh; padding: var(--space-8); }
    .box { width: 100%; max-width: 420px; }
    .box label { display: block; font-size: var(--font-size-sm); }
    .err { color: var(--red); font-size: var(--font-size-sm); }
  `],
})
export default class LoginPage {
  private readonly fb = inject(FormBuilder);
  private readonly auth = inject(AuthState);
  private readonly router = inject(Router);

  readonly loading = signal(false);
  readonly error = signal<string | null>(null);

  readonly form = this.fb.nonNullable.group({
    email: ['', [Validators.required, Validators.email]],
    password: ['', [Validators.required, Validators.minLength(10)]],
  });

  async submit(): Promise<void> {
    if (this.form.invalid) return;
    this.loading.set(true);
    this.error.set(null);
    try {
      await this.auth.login(this.form.value.email!, this.form.value.password!);
      await this.router.navigateByUrl('/app/dashboard');
    } catch {
      this.error.set('Credenciales invalidas o error de red.');
    } finally {
      this.loading.set(false);
    }
  }
}
