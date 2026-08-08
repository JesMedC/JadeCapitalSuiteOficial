import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { Router, RouterLink, RouterLinkActive } from '@angular/router';
import { AuthState } from '@core/state/auth.state';

@Component({
  selector: 'jcs-register',
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
          <a routerLink="/auth/login" class="jcs-btn jcs-btn--ghost jcs-btn--sm">Login</a>
          <a routerLink="/auth/register" class="jcs-btn jcs-btn--primary jcs-btn--sm">Registrarse</a>
        </div>
      </div>
    </header>

    <!-- ============== Ticker tape (mismo que login) ============== -->
    <div class="ticker" aria-hidden="true">
      <div class="ticker-track">
        @for (i of [0,1]; track i) {
          <span class="ticker-row">
            <span class="tick"><b>EUR/USD</b><span class="up">1.0872</span><span class="up jcs-num">+0.34%</span></span>
            <span class="tick"><b>GBP/USD</b><span class="up">1.2641</span><span class="up jcs-num">+0.18%</span></span>
            <span class="tick"><b>USD/JPY</b><span class="dn">154.27</span><span class="dn jcs-num">-0.21%</span></span>
            <span class="tick"><b>XAU/USD</b><span class="up">2,348.40</span><span class="up jcs-num">+0.82%</span></span>
            <span class="tick"><b>BTC/USD</b><span class="dn">66,420</span><span class="dn jcs-num">-1.12%</span></span>
            <span class="tick"><b>ETH/USD</b><span class="up">3,182</span><span class="up jcs-num">+0.45%</span></span>
            <span class="tick"><b>USD/CHF</b><span class="up">0.9012</span><span class="up jcs-num">+0.09%</span></span>
            <span class="tick"><b>AUD/USD</b><span class="dn">0.6614</span><span class="dn jcs-num">-0.27%</span></span>
          </span>
        }
      </div>
    </div>

    <!-- ============== Hero + chart background ============== -->
    <section class="hero">
      <svg class="bg-chart" viewBox="0 0 1440 600" preserveAspectRatio="none" aria-hidden="true">
        <defs>
          <linearGradient id="regChartGrad" x1="0" x2="0" y1="0" y2="1">
            <stop offset="0%" stop-color="#2FDB78" stop-opacity="0.18"/>
            <stop offset="100%" stop-color="#2FDB78" stop-opacity="0"/>
          </linearGradient>
          <linearGradient id="regChartLine" x1="0" x2="1" y1="0" y2="0">
            <stop offset="0%" stop-color="#2FDB78" stop-opacity="0.0"/>
            <stop offset="50%" stop-color="#2FDB78" stop-opacity="0.6"/>
            <stop offset="100%" stop-color="#2FDB78" stop-opacity="0.0"/>
          </linearGradient>
        </defs>
        <g class="bg-grid" stroke="#1C2A33" stroke-width="1">
          <line x1="0" y1="100" x2="1440" y2="100"/>
          <line x1="0" y1="200" x2="1440" y2="200"/>
          <line x1="0" y1="300" x2="1440" y2="300"/>
          <line x1="0" y1="400" x2="1440" y2="400"/>
          <line x1="0" y1="500" x2="1440" y2="500"/>
        </g>
        <path class="bg-area"
          d="M0,400 C120,380 200,420 280,380 C360,340 440,360 520,320 C600,280 680,300 760,240 C840,180 920,200 1000,220 C1080,240 1160,180 1240,200 C1320,220 1400,160 1440,140 L1440,600 L0,600 Z"
          fill="url(#regChartGrad)"/>
        <path class="bg-line"
          d="M0,400 C120,380 200,420 280,380 C360,340 440,360 520,320 C600,280 680,300 760,240 C840,180 920,200 1000,220 C1080,240 1160,180 1240,200 C1320,220 1400,160 1440,140"
          fill="none" stroke="url(#regChartLine)" stroke-width="2.5"/>
      </svg>

      <div class="jcs-container hero-grid">
        <!-- ============== Columna izquierda: copy + features ============== -->
        <div class="hero-copy">
          <span class="hero-chip">
            <span class="chip-dot" aria-hidden="true"></span>
            14 días gratis
          </span>
          <h1 class="hero-title">
            Crea tu<br>
            <span class="hero-accent">cuenta</span>
          </h1>
          <p class="hero-lede">
            Empieza a registrar tus operaciones, controlar saldos y
            analizar tu P&amp;L en menos de 60 segundos.
          </p>

          <ul class="hero-features">
            <li>
              <span class="feat-icon" aria-hidden="true">
                <svg viewBox="0 0 24 24" width="20" height="20" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
                  <path d="M12 2v6"/>
                  <path d="M12 22v-6"/>
                  <path d="M4.93 4.93l4.24 4.24"/>
                  <path d="M14.83 14.83l4.24 4.24"/>
                  <path d="M2 12h6"/>
                  <path d="M22 12h-6"/>
                  <path d="M4.93 19.07l4.24-4.24"/>
                  <path d="M14.83 9.17l4.24-4.24"/>
                </svg>
              </span>
              <div class="feat-text">
                <strong>Prueba 14 días gratis</strong>
                <span class="jcs-muted">Acceso completo a todas las funciones, sin tarjeta de crédito.</span>
              </div>
            </li>
            <li>
              <span class="feat-icon" aria-hidden="true">
                <svg viewBox="0 0 24 24" width="20" height="20" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
                  <rect x="3" y="11" width="18" height="11" rx="2"/>
                  <path d="M7 11V7a5 5 0 0 1 10 0v4"/>
                </svg>
              </span>
              <div class="feat-text">
                <strong>Sin tarjeta de crédito</strong>
                <span class="jcs-muted">Te registrás y empezás. Cobramos solo cuando elegís un plan.</span>
              </div>
            </li>
            <li>
              <span class="feat-icon" aria-hidden="true">
                <svg viewBox="0 0 24 24" width="20" height="20" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
                  <path d="M3 12a9 9 0 1 0 9-9"/>
                  <polyline points="3 4 3 9 8 9"/>
                </svg>
              </span>
              <div class="feat-text">
                <strong>Cancela cuando quieras</strong>
                <span class="jcs-muted">Sin contratos ni permanencias. Tus datos son tuyos.</span>
              </div>
            </li>
          </ul>
        </div>

        <!-- ============== Columna derecha: card de registro ============== -->
        <aside class="register-card-wrap">
          <div class="jcs-card jcs-card--glow register-card">
            <header class="register-head">
              <span class="register-icon" aria-hidden="true">
                <svg viewBox="0 0 24 24" width="22" height="22" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
                  <path d="M16 21v-2a4 4 0 0 0-4-4H5a4 4 0 0 0-4 4v2"/>
                  <circle cx="8.5" cy="7" r="4"/>
                  <line x1="20" y1="8" x2="20" y2="14"/>
                  <line x1="23" y1="11" x2="17" y2="11"/>
                </svg>
              </span>
              <h2 class="register-title">Crear cuenta</h2>
              <p class="jcs-muted register-sub">Empieza tu prueba gratuita de 14 días</p>
            </header>

            <form [formGroup]="form" (ngSubmit)="submit()" class="register-form">
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
                <label class="jcs-label" for="displayName">Nombre</label>
                <input
                  id="displayName"
                  class="jcs-input"
                  type="text"
                  formControlName="displayName"
                  autocomplete="name"
                  placeholder="Tu nombre"
                  [class.jcs-input--error]="form.controls.displayName.touched && form.controls.displayName.invalid">
              </div>

              <div class="field">
                <div class="field-row">
                  <label class="jcs-label" for="password">Contraseña</label>
                  <span class="hint jcs-muted">Mín. 10, letras y números</span>
                </div>
                <input
                  id="password"
                  class="jcs-input"
                  type="password"
                  formControlName="password"
                  autocomplete="new-password"
                  placeholder="••••••••"
                  [class.jcs-input--error]="form.controls.password.touched && form.controls.password.invalid">
              </div>

              @if (error()) {
                <div class="jcs-error register-error">{{ error() }}</div>
              }

              <button
                class="jcs-btn jcs-btn--primary jcs-btn--lg jcs-btn--block"
                type="submit"
                [disabled]="form.invalid || loading()">
                {{ loading() ? 'Creando…' : 'Crear cuenta' }}
              </button>

              <div class="separator"><span>o</span></div>

              <a routerLink="/auth/login" class="jcs-btn jcs-btn--ghost jcs-btn--lg jcs-btn--block">
                Ya tengo cuenta
              </a>
            </form>

            <p class="register-foot jcs-muted">
              Al registrarte, aceptas nuestros
              <a href="#">Términos de servicio</a> y
              <a href="#">Política de privacidad</a>.
            </p>
          </div>
        </aside>
      </div>

      <!-- ============== Trial preview strip ============== -->
      <div class="jcs-container stats-strip">
        <div class="stats-card">
          <header class="stats-head">
            <span class="stats-title">Tu prueba incluye <span class="jcs-muted">/ acceso completo</span></span>
            <span class="stats-period">14 días restantes</span>
          </header>
          <div class="stats-grid">
            <div class="stat">
              <span class="stat-label">TRADES ILIMITADOS</span>
              <svg class="check-icon" viewBox="0 0 24 24" width="24" height="24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
                <polyline points="20 6 9 17 4 12"/>
              </svg>
              <span class="stat-foot jcs-muted">Sin tope de operaciones</span>
            </div>
            <div class="stat">
              <span class="stat-label">DASHBOARD</span>
              <svg class="check-icon" viewBox="0 0 24 24" width="24" height="24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
                <polyline points="20 6 9 17 4 12"/>
              </svg>
              <span class="stat-foot jcs-muted">P&L, win rate, drawdown</span>
            </div>
            <div class="stat">
              <span class="stat-label">CALENDARIO</span>
              <svg class="check-icon" viewBox="0 0 24 24" width="24" height="24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
                <polyline points="20 6 9 17 4 12"/>
              </svg>
              <span class="stat-foot jcs-muted">Visualizá tus días verdes</span>
            </div>
            <div class="stat">
              <span class="stat-label">SOPORTE</span>
              <svg class="check-icon" viewBox="0 0 24 24" width="24" height="24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
                <polyline points="20 6 9 17 4 12"/>
              </svg>
              <span class="stat-foot jcs-muted">Respuesta en 24h</span>
            </div>
          </div>
        </div>
      </div>
    </section>
  `,
  styles: [`
    :host { display: block; min-height: 100vh; background: var(--bg-main); overflow: hidden; }

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

    /* Ticker tape. */
    .ticker {
      position: relative;
      background: linear-gradient(180deg, rgba(8,16,24,0.7) 0%, rgba(8,16,24,0.3) 100%);
      border-bottom: 1px solid var(--border-soft);
      overflow: hidden;
      padding: var(--sp-2) 0;
      z-index: 5;
    }
    .ticker-track {
      display: flex;
      width: max-content;
      animation: ticker-scroll 60s linear infinite;
    }
    .ticker-row {
      display: flex;
      gap: var(--sp-8);
      padding-right: var(--sp-8);
      flex-shrink: 0;
    }
    .tick {
      display: inline-flex;
      gap: var(--sp-2);
      font-size: var(--fs-xs);
      font-family: var(--font-mono);
      white-space: nowrap;
    }
    .tick b { color: var(--text-secondary); font-weight: 600; }
    .tick .up { color: var(--green); }
    .tick .dn { color: var(--text-main); }
    @keyframes ticker-scroll {
      from { transform: translateX(0); }
      to   { transform: translateX(-50%); }
    }
    .ticker:hover .ticker-track { animation-play-state: paused; }

    /* Hero + chart background. */
    .hero {
      position: relative;
      padding: var(--sp-16) 0 var(--sp-20);
      min-height: calc(100vh - 72px - 32px);
      display: flex;
      flex-direction: column;
      justify-content: center;
    }
    .bg-chart {
      position: absolute;
      inset: 0;
      width: 100%;
      height: 100%;
      pointer-events: none;
      z-index: 0;
    }
    .bg-grid { opacity: 0.4; }
    .bg-area {
      animation: bg-pulse 8s ease-in-out infinite;
    }
    .bg-line {
      stroke-dasharray: 2000;
      stroke-dashoffset: 2000;
      animation: bg-draw 6s ease-out forwards, bg-pulse 8s 6s ease-in-out infinite;
    }
    @keyframes bg-draw {
      to { stroke-dashoffset: 0; }
    }
    @keyframes bg-pulse {
      0%, 100% { opacity: 0.85; }
      50%      { opacity: 1; }
    }
    .hero-grid {
      position: relative;
      z-index: 1;
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

    .hero-chip {
      display: inline-flex;
      align-items: center;
      gap: var(--sp-2);
      padding: var(--sp-1) var(--sp-3);
      background: var(--green-soft);
      color: var(--green);
      border: 1px solid var(--border-active);
      border-radius: 999px;
      font-size: var(--fs-xs);
      font-weight: 600;
      margin-bottom: var(--sp-4);
      animation: fade-up 0.6s ease-out both;
    }
    .chip-dot {
      width: 6px; height: 6px;
      border-radius: 999px;
      background: var(--green);
      box-shadow: 0 0 0 0 rgba(47, 219, 120, 0.6);
      animation: chip-pulse 1.8s ease-out infinite;
    }
    @keyframes chip-pulse {
      0%   { box-shadow: 0 0 0 0 rgba(47, 219, 120, 0.6); }
      70%  { box-shadow: 0 0 0 8px rgba(47, 219, 120, 0); }
      100% { box-shadow: 0 0 0 0 rgba(47, 219, 120, 0); }
    }

    .hero-title {
      font-size: var(--fs-5xl);
      line-height: 1.05;
      margin-bottom: var(--sp-6);
      animation: fade-up 0.6s 0.05s ease-out both;
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
      animation: fade-up 0.6s 0.1s ease-out both;
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
      animation: fade-up 0.6s ease-out both;
    }
    .hero-features li:nth-child(1) { animation-delay: 0.2s; }
    .hero-features li:nth-child(2) { animation-delay: 0.3s; }
    .hero-features li:nth-child(3) { animation-delay: 0.4s; }

    @keyframes fade-up {
      from { opacity: 0; transform: translateY(12px); }
      to   { opacity: 1; transform: translateY(0); }
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
      transition: transform 200ms ease, box-shadow 200ms ease;
    }
    .hero-features li:hover .feat-icon {
      transform: scale(1.08) rotate(-3deg);
      box-shadow: var(--shadow-glow);
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

    /* Card de registro. */
    .register-card-wrap {
      display: flex;
      justify-content: center;
      animation: fade-up 0.6s 0.2s ease-out both;
    }
    .register-card {
      width: 100%;
      max-width: 440px;
      padding: var(--sp-8);
      position: relative;
      transition: transform 250ms ease, box-shadow 250ms ease;
    }
    .register-card:hover {
      transform: translateY(-2px);
      box-shadow: 0 0 48px rgba(47, 219, 120, 0.22);
    }
    .register-card::before {
      content: '';
      position: absolute;
      inset: -1px;
      border-radius: var(--radius-md);
      padding: 1px;
      background: linear-gradient(120deg,
        rgba(47, 219, 120, 0.4) 0%,
        rgba(47, 219, 120, 0.0) 35%,
        rgba(47, 219, 120, 0.0) 65%,
        rgba(47, 219, 120, 0.4) 100%);
      -webkit-mask: linear-gradient(#000 0 0) content-box, linear-gradient(#000 0 0);
      -webkit-mask-composite: xor;
              mask-composite: exclude;
      animation: shimmer 5s linear infinite;
      pointer-events: none;
    }
    @keyframes shimmer {
      from { background-position: -200% 0; }
      to   { background-position: 200% 0; }
    }
    .register-head {
      display: flex;
      flex-direction: column;
      align-items: center;
      text-align: center;
      margin-bottom: var(--sp-6);
    }
    .register-icon {
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
      animation: pulse-glow 2.4s ease-in-out infinite;
    }
    @keyframes pulse-glow {
      0%, 100% { box-shadow: 0 0 0 0 rgba(47, 219, 120, 0.0); }
      50%      { box-shadow: 0 0 0 8px rgba(47, 219, 120, 0.08); }
    }
    .register-title {
      font-size: var(--fs-2xl);
      font-weight: 700;
      margin-bottom: var(--sp-2);
    }
    .register-sub {
      font-size: var(--fs-sm);
      margin: 0;
    }

    .register-form {
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
    .hint {
      font-size: var(--fs-xs);
    }

    .register-error {
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

    .register-foot {
      text-align: center;
      font-size: var(--fs-xs);
      margin: var(--sp-6) 0 0;
      line-height: 1.6;
    }

    /* ===== Trial preview strip ===== */
    .stats-strip {
      position: relative;
      z-index: 1;
      margin-top: var(--sp-12);
      animation: fade-up 0.6s 0.5s ease-out both;
    }
    .stats-card {
      background: rgba(14, 22, 32, 0.7);
      border: 1px solid var(--border);
      border-radius: var(--radius-md);
      padding: var(--sp-5) var(--sp-6);
      backdrop-filter: blur(8px);
    }
    .stats-head {
      display: flex;
      justify-content: space-between;
      align-items: center;
      margin-bottom: var(--sp-4);
    }
    .stats-title {
      font-size: var(--fs-sm);
      font-weight: 600;
      color: var(--text-main);
    }
    .stats-period {
      font-size: var(--fs-xs);
      color: var(--green);
      font-family: var(--font-mono);
      font-weight: 600;
    }
    .stats-grid {
      display: grid;
      grid-template-columns: repeat(4, 1fr);
      gap: var(--sp-4);
    }
    @media (max-width: 720px) {
      .stats-grid { grid-template-columns: repeat(2, 1fr); }
    }
    .stat {
      display: flex;
      flex-direction: column;
      gap: var(--sp-2);
      padding: var(--sp-3);
      border-radius: var(--radius-sm);
      transition: background 200ms;
    }
    .stat:hover { background: rgba(255, 255, 255, 0.02); }
    .stat-label {
      font-size: 0.65rem;
      letter-spacing: 0.08em;
      color: var(--text-muted);
      font-weight: 600;
    }
    .check-icon {
      color: var(--green);
      transition: transform 200ms;
    }
    .stat:hover .check-icon {
      transform: scale(1.15);
    }
    .stat-foot {
      font-size: var(--fs-xs);
      font-family: var(--font-mono);
    }
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
