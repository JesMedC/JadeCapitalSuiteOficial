import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { Router, RouterLink, RouterLinkActive } from '@angular/router';
import { AuthState } from '@core/state/auth.state';

@Component({
  selector: 'jcs-login',
  standalone: true,
  imports: [ReactiveFormsModule, RouterLink, RouterLinkActive],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <!-- ============== Top bar (mismo que landing) ============== -->
    <header class="topbar">
      <div class="jcs-container nav">
        <a class="brand" routerLink="/">
          <span class="brand-mark" aria-hidden="true">
            <svg viewBox="0 0 24 24" width="22" height="22" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
              <path d="M3 3v18h18"/>
              <path d="M7 14l4-4 3 3 5-6"/>
            </svg>
          </span>
          <span class="brand-name">JadeCapital<strong>Suite</strong> <span class="jcs-badge jcs-badge--neutral brand-tag">PRO</span></span>
        </a>
        <nav class="nav-links">
          <a routerLink="/" fragment="top">Home</a>
          <a routerLink="/" fragment="nosotros">Nosotros</a>
          <a routerLink="/" fragment="planes">Planes</a>
          <a routerLink="/" fragment="contacto">Contacto</a>
        </nav>
        <div class="nav-cta">
          <a routerLink="/auth/login" class="jcs-btn jcs-btn--ghost jcs-btn--sm active">Login</a>
          <a routerLink="/auth/register" class="jcs-btn jcs-btn--primary jcs-btn--sm">Registrarse</a>
        </div>
      </div>
    </header>

    <!-- ============== Hero: 2 columnas ============== -->
    <section class="hero">
      <div class="jcs-container hero-grid">
        <!-- ============== Columna izquierda: copy + features ============== -->
        <div class="hero-copy">
          <h1 class="hero-title">
            Bienvenido<br>
            <span class="hero-accent">de nuevo</span>
          </h1>
          <p class="hero-lede">
            Accede de forma segura a tu panel de trading y continúa
            operando con criterio.
          </p>

          <ul class="hero-features">
            <li>
              <span class="feat-icon" aria-hidden="true">
                <svg viewBox="0 0 24 24" width="20" height="20" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
                  <rect x="3" y="11" width="18" height="11" rx="2"/>
                  <path d="M7 11V7a5 5 0 0 1 10 0v4"/>
                </svg>
              </span>
              <div class="feat-text">
                <strong>Tus datos están encriptados</strong>
                <span class="jcs-muted">Tus credenciales y registros se guardan con hashing PBKDF2 + sal aleatoria.</span>
              </div>
            </li>
            <li>
              <span class="feat-icon" aria-hidden="true">
                <svg viewBox="0 0 24 24" width="20" height="20" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
                  <path d="M13 2L3 14h9l-1 8 10-12h-9l1-8z"/>
                </svg>
              </span>
              <div class="feat-text">
                <strong>Acceso rápido y seguro</strong>
                <span class="jcs-muted">JWT firmado HS256 con tokens de acceso (15 min) y refresh rotativo (14 días).</span>
              </div>
            </li>
            <li>
              <span class="feat-icon" aria-hidden="true">
                <svg viewBox="0 0 24 24" width="20" height="20" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
                  <circle cx="12" cy="12" r="10"/>
                  <polyline points="12 6 12 12 16 14"/>
                </svg>
              </span>
              <div class="feat-text">
                <strong>Control de check-in diario</strong>
                <span class="jcs-muted">Estadísticas y dashboard actualizan cada vez que operas.</span>
              </div>
            </li>
          </ul>
        </div>

        <!-- ============== Columna derecha: card de login ============== -->
        <aside class="login-card-wrap">
          <div class="jcs-card jcs-card--glow login-card">
            <header class="login-head">
              <span class="login-icon" aria-hidden="true">
                <svg viewBox="0 0 24 24" width="22" height="22" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
                  <rect x="3" y="11" width="18" height="11" rx="2"/>
                  <path d="M7 11V7a5 5 0 0 1 10 0v4"/>
                </svg>
              </span>
              <h2 class="login-title">Iniciar sesión</h2>
              <p class="jcs-muted login-sub">Accede a tu cuenta para continuar</p>
            </header>

            <form [formGroup]="form" (ngSubmit)="submit()" class="login-form">
              <div class="field">
                <label class="jcs-label" for="email">Correo electrónico</label>
                <input
                  id="email"
                  class="jcs-input"
                  type="email"
                  formControlName="email"
                  autocomplete="email"
                  placeholder="tu@email.com"
                  [class.jcs-input--error]="form.controls.email.touched && form.controls.email.invalid">
              </div>

              <div class="field">
                <div class="field-row">
                  <label class="jcs-label" for="password">Contraseña</label>
                  <a class="forgot" href="#" tabindex="-1">¿Olvidaste tu contraseña?</a>
                </div>
                <input
                  id="password"
                  class="jcs-input"
                  type="password"
                  formControlName="password"
                  autocomplete="current-password"
                  placeholder="••••••••"
                  [class.jcs-input--error]="form.controls.password.touched && form.controls.password.invalid">
              </div>

              @if (error()) {
                <div class="jcs-error login-error">{{ error() }}</div>
              }

              <button
                class="jcs-btn jcs-btn--primary jcs-btn--lg jcs-btn--block"
                type="submit"
                [disabled]="form.invalid || loading()">
                {{ loading() ? 'Entrando…' : 'Iniciar sesión' }}
              </button>

              <div class="separator"><span>o</span></div>

              <a routerLink="/auth/register" class="jcs-btn jcs-btn--ghost jcs-btn--lg jcs-btn--block">
                Crear cuenta
              </a>
            </form>

            <p class="login-foot jcs-muted">
              Al iniciar sesión, aceptas nuestros
              <a href="#">Términos de servicio</a> y
              <a href="#">Política de privacidad</a>.
            </p>
          </div>
        </aside>
      </div>
    </section>
  `,
  styles: [`
    :host { display: block; min-height: 100vh; background: var(--bg-main); }

    /* Topbar (mismo patrón que landing). */
    .topbar {
      position: sticky;
      top: 0;
      z-index: 10;
      background: rgba(5, 11, 16, 0.85);
      backdrop-filter: blur(12px);
      border-bottom: 1px solid var(--border-soft);
    }
    .nav {
      display: flex; align-items: center; justify-content: space-between;
      gap: var(--sp-6);
      padding: var(--sp-4) var(--sp-6);
    }
    .brand {
      display: inline-flex; align-items: center; gap: var(--sp-3);
      color: var(--text-main);
    }
    .brand-mark {
      display: inline-flex; align-items: center; justify-content: center;
      width: 36px; height: 36px;
      background: var(--green-soft);
      color: var(--green);
      border-radius: var(--radius-sm);
      border: 1px solid var(--border-active);
    }
    .brand-name { font-weight: 600; letter-spacing: -0.02em; display: inline-flex; align-items: center; gap: var(--sp-2); }
    .brand-name strong { color: var(--green); font-weight: 700; }
    .brand-tag { font-size: 0.65rem; }
    .nav-links { display: flex; gap: var(--sp-6); }
    .nav-links a {
      color: var(--text-secondary);
      font-size: var(--fs-sm);
      font-weight: 500;
      transition: color 150ms;
    }
    .nav-links a:hover, .nav-links a.active { color: var(--text-main); }
    .nav-cta { display: flex; gap: var(--sp-3); }
    .nav-cta a.active { color: var(--green); border-color: var(--border-active); }

    /* Hero 2 columnas. */
    .hero {
      padding: var(--sp-16) 0 var(--sp-20);
      min-height: calc(100vh - 72px);
      display: flex;
      align-items: center;
    }
    .hero-grid {
      display: grid;
      grid-template-columns: 1.1fr 1fr;
      gap: var(--sp-16);
      align-items: center;
    }

    @media (max-width: 960px) {
      .hero-grid { grid-template-columns: 1fr; gap: var(--sp-10); }
      .hero { padding: var(--sp-10) 0; }
      .nav-links { display: none; }
    }

    .hero-title {
      font-size: var(--fs-5xl);
      line-height: 1.05;
      margin-bottom: var(--sp-6);
    }
    .hero-accent {
      color: var(--green);
      background: linear-gradient(135deg, var(--green) 0%, var(--green-hover) 100%);
      -webkit-background-clip: text;
      background-clip: text;
      -webkit-text-fill-color: transparent;
    }
    .hero-lede {
      font-size: var(--fs-lg);
      color: var(--text-secondary);
      line-height: 1.55;
      margin-bottom: var(--sp-8);
      max-width: 480px;
    }

    .hero-features {
      list-style: none;
      padding: 0;
      margin: 0;
      display: flex;
      flex-direction: column;
      gap: var(--sp-5);
    }
    .hero-features li {
      display: flex;
      gap: var(--sp-4);
      align-items: flex-start;
    }
    .feat-icon {
      flex-shrink: 0;
      display: inline-flex;
      align-items: center;
      justify-content: center;
      width: 40px;
      height: 40px;
      border-radius: var(--radius-sm);
      background: var(--green-soft);
      color: var(--green);
      border: 1px solid var(--border-active);
    }
    .feat-text {
      display: flex;
      flex-direction: column;
      gap: var(--sp-1);
    }
    .feat-text strong {
      font-weight: 600;
      color: var(--text-main);
      font-size: var(--fs-base);
    }
    .feat-text span {
      font-size: var(--fs-sm);
      line-height: 1.5;
    }

    /* Card de login. */
    .login-card-wrap {
      display: flex;
      justify-content: center;
    }
    .login-card {
      width: 100%;
      max-width: 440px;
      padding: var(--sp-8);
    }
    .login-head {
      display: flex;
      flex-direction: column;
      align-items: center;
      text-align: center;
      margin-bottom: var(--sp-6);
    }
    .login-icon {
      display: inline-flex;
      align-items: center;
      justify-content: center;
      width: 48px;
      height: 48px;
      border-radius: var(--radius-md);
      background: var(--green-soft);
      color: var(--green);
      border: 1px solid var(--border-active);
      margin-bottom: var(--sp-4);
    }
    .login-title {
      font-size: var(--fs-2xl);
      font-weight: 700;
      margin-bottom: var(--sp-2);
    }
    .login-sub {
      font-size: var(--fs-sm);
      margin: 0;
    }

    .login-form {
      display: flex;
      flex-direction: column;
      gap: var(--sp-4);
    }
    .field { display: flex; flex-direction: column; gap: var(--sp-2); }
    .field-row {
      display: flex;
      align-items: center;
      justify-content: space-between;
    }
    .field-row .jcs-label { margin-bottom: 0; }
    .forgot {
      font-size: var(--fs-xs);
      color: var(--text-muted);
    }
    .forgot:hover { color: var(--green); }

    .login-error {
      background: rgba(255, 64, 87, 0.08);
      border: 1px solid rgba(255, 64, 87, 0.3);
      padding: var(--sp-3);
      border-radius: var(--radius-sm);
      margin-top: 0;
    }

    .separator {
      display: flex;
      align-items: center;
      gap: var(--sp-3);
      color: var(--text-soft);
      font-size: var(--fs-xs);
      text-transform: uppercase;
      letter-spacing: 0.1em;
      margin: var(--sp-2) 0;
    }
    .separator::before, .separator::after {
      content: '';
      flex: 1;
      height: 1px;
      background: var(--border-soft);
    }

    .login-foot {
      text-align: center;
      font-size: var(--fs-xs);
      margin: var(--sp-6) 0 0;
      line-height: 1.6;
    }
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
      this.error.set('Credenciales inválidas o error de red.');
    } finally {
      this.loading.set(false);
    }
  }
}
