import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { HttpClient } from '@angular/common/http';
import { Router, RouterLink } from '@angular/router';

@Component({
  selector: 'jcs-forgot-password',
  standalone: true,
  imports: [ReactiveFormsModule, RouterLink],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="jcs-container recovery-shell">
      <a class="back" routerLink="/auth/login" aria-label="Volver a iniciar sesión">← Iniciar sesión</a>
      <div class="jcs-card jcs-card--glow recovery-card">
        <h1 class="recovery-title">Recuperar contraseña</h1>
        <p class="recovery-sub">Te enviaremos un correo con una contraseña temporal si la cuenta existe.</p>
        <form [formGroup]="form" (ngSubmit)="submit()" novalidate>
          <label class="jcs-label" for="email">Correo electrónico</label>
          <input id="email" class="jcs-input" type="email" formControlName="email"
                 autocomplete="email" placeholder="tu@email.com" aria-describedby="status"
                 [class.jcs-input--error]="form.controls.email.touched && form.controls.email.invalid" />
          @if (form.controls.email.touched && form.controls.email.invalid) {
            <small class="jcs-error">Introduce un correo válido.</small>
          }
          <button class="jcs-btn jcs-btn--primary" type="submit"
                  [disabled]="loading() || form.invalid" aria-busy="{{ loading() }}">
            {{ loading() ? 'Enviando…' : 'Enviar instrucciones' }}
          </button>
        </form>
        <p id="status" class="recovery-status" role="status" aria-live="polite">{{ status() }}</p>
      </div>
    </div>
  `,
  styles: [`
    .recovery-shell { max-width: 480px; padding: 64px 24px; }
    .back { display: inline-block; margin-bottom: 16px; color: var(--green); text-decoration: none; }
    .back:hover { text-decoration: underline; }
    .recovery-card { padding: 32px; }
    .recovery-title { font-size: 1.5rem; margin: 0 0 8px; }
    .recovery-sub { color: var(--muted); margin: 0 0 24px; font-size: 0.9rem; }
    form { display: flex; flex-direction: column; gap: 12px; }
    .recovery-status { min-height: 1.25em; margin: 16px 0 0; font-size: 0.9rem; }
    .recovery-status:not(:empty) { color: var(--green); }
    .jcs-error { color: #a92323; font-size: 0.85rem; }
    @media (max-width: 360px) {
      .recovery-shell { padding: 32px 12px; }
      .recovery-card { padding: 20px 16px; }
    }
  `],
})
export class ForgotPasswordPage {
  private readonly fb = inject(FormBuilder);
  private readonly http = inject(HttpClient);
  private readonly router = inject(Router);

  readonly form = this.fb.nonNullable.group({
    email: ['', [Validators.required, Validators.email]],
  });

  readonly loading = signal(false);
  readonly status = signal('');

  submit(): void {
    if (this.form.invalid || this.loading()) return;
    const email = this.form.controls.email.value.trim();
    this.loading.set(true);
    this.status.set('');
    this.http.post('/api/auth/forgot-password', { email }).subscribe({
      next: () => {
        this.loading.set(false);
        this.status.set('Si el correo está registrado, recibirás las instrucciones en breve.');
        this.form.reset();
      },
      error: () => {
        this.loading.set(false);
        this.status.set('Si el correo está registrado, recibirás las instrucciones en breve.');
      },
    });
  }
}
