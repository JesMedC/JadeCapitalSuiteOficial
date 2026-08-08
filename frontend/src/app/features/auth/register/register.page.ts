import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { AuthState } from '@core/state/auth.state';

@Component({
  selector: 'jcs-register',
  standalone: true,
  imports: [ReactiveFormsModule, RouterLink],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="shell">
      <div class="jcs-card box">
        <h2>Crear cuenta</h2>
        <form [formGroup]="form" (ngSubmit)="submit()" class="jcs-stack">
          <label>Email
            <input class="jcs-input" type="email" formControlName="email" autocomplete="email">
          </label>
          <label>Nombre
            <input class="jcs-input" type="text" formControlName="displayName">
          </label>
          <label>Contrasena (min. 10, letras y digitos)
            <input class="jcs-input" type="password" formControlName="password" autocomplete="new-password">
          </label>
          @if (error()) { <div class="err">{{ error() }}</div> }
          <button class="jcs-btn jcs-btn--primary" type="submit" [disabled]="form.invalid || loading()">
            {{ loading() ? 'Creando...' : 'Crear cuenta' }}
          </button>
        </form>
        <p class="jcs-muted">Ya tienes cuenta? <a routerLink="/auth/login">Inicia sesion</a></p>
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
export default class RegisterPage {
  private readonly fb = inject(FormBuilder);
  private readonly auth = inject(AuthState);
  private readonly router = inject(Router);

  readonly loading = signal(false);
  readonly error = signal<string | null>(null);

  readonly form = this.fb.nonNullable.group({
    email: ['', [Validators.required, Validators.email]],
    displayName: ['', [Validators.required, Validators.minLength(2), Validators.maxLength(80)]],
    password: ['', [Validators.required, Validators.minLength(10), Validators.maxLength(128)]],
  });

  async submit(): Promise<void> {
    if (this.form.invalid) return;
    this.loading.set(true);
    this.error.set(null);
    try {
      await this.auth.register(this.form.value.email!, this.form.value.displayName!, this.form.value.password!);
      await this.router.navigateByUrl('/app/dashboard');
    } catch {
      this.error.set('No se pudo crear la cuenta.');
    } finally {
      this.loading.set(false);
    }
  }
}
