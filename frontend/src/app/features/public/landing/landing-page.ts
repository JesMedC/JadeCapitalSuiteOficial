import { ChangeDetectionStrategy, Component, DestroyRef, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { RouterLink } from '@angular/router';
import { from } from 'rxjs';
import { PlanApiService } from '@core/api/plan-api.service';
import { PlanInfo } from '@core/api/plan-info';
import { AuthState } from '@core/state/auth.state';

interface Feature {
  title: string;
  body: string;
  iconPath: string;
}
interface Plan {
  code: string;
  name: string;
  monthly: number;
  annual: number;
  blurb: string;
  features: readonly string[];
  highlight: boolean;
}
interface Faq { q: string; a: string; }

// Marketing copy per plan code. Backend exposes only price/name/currency
// via GET /api/billing/plans — the bullet lists, highlight flag and blurb
// are owned by the landing-page and merged at render time. Annual price is
// a marketing-derived number (20% off monthly) computed locally; the
// backend does not (yet) expose it.
const PLAN_MARKETING: Record<string, { features: readonly string[]; blurb: string; highlight: boolean; annualDiscount: number }> = {
  starter: { features: ['1 cuenta', 'Registro ilimitado de operaciones', 'Reportes basicos', 'Soporte por email'],         blurb: 'Para traders que comienzan.',     highlight: false, annualDiscount: 0.20 },
  pro:     { features: ['Hasta 5 cuentas', 'Metricas avanzadas y filtros', 'Reportes personalizados', 'Exportacion de datos (CSV)', 'Soporte prioritario'], blurb: 'Para traders que quieren crecer.', highlight: true,  annualDiscount: 0.20 },
  elite:   { features: ['Cuentas ilimitadas', 'Analisis avanzado de rendimiento', 'Backtesting de estrategias', 'Alertas y objetivos personalizados', 'Soporte VIP'],     blurb: 'Para traders exigentes.',         highlight: false, annualDiscount: 0.20 },
};

@Component({
  selector: 'jcs-landing-page',
  standalone: true,
  imports: [RouterLink],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <!-- ============== Top bar ============== -->
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
          <a routerLink="/" fragment="top" class="active">Home</a>
          <a routerLink="/" fragment="nosotros">Nosotros</a>
          <a routerLink="/" fragment="planes">Planes</a>
          <a routerLink="/" fragment="contacto">Contacto</a>
        </nav>
        <div class="nav-cta">
          <a routerLink="/auth/login" class="jcs-btn jcs-btn--ghost jcs-btn--sm nav-cta-login">Login</a>
          @if (auth.isAuthenticated()) {
            <a routerLink="/app/dashboard" class="jcs-btn jcs-btn--primary jcs-btn--sm">Dashboard</a>
          } @else {
            <a routerLink="/auth/register" class="jcs-btn jcs-btn--primary jcs-btn--sm">Registrarse</a>
          }
          <!-- Hamburger button — mobile only. -->
          <button
            type="button"
            class="hamburger"
            (click)="toggleMobileNav()"
            [attr.aria-expanded]="mobileNavOpen()"
            aria-controls="landing-mobile-nav"
            [attr.aria-label]="mobileNavOpen() ? 'Cerrar menú' : 'Abrir menú'">
            @if (mobileNavOpen()) {
              <svg viewBox="0 0 24 24" width="22" height="22" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true">
                <line x1="18" y1="6" x2="6" y2="18"/>
                <line x1="6" y1="6" x2="18" y2="18"/>
              </svg>
            } @else {
              <svg viewBox="0 0 24 24" width="22" height="22" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true">
                <line x1="3" y1="6" x2="21" y2="6"/>
                <line x1="3" y1="12" x2="21" y2="12"/>
                <line x1="3" y1="18" x2="21" y2="18"/>
              </svg>
            }
          </button>
        </div>
      </div>
      <!-- Mobile drawer — hidden on tablet+. Closes on backdrop click. -->
      @if (mobileNavOpen()) {
        <div class="mobile-drawer-backdrop" (click)="closeMobileNav()" aria-hidden="true"></div>
        <nav id="landing-mobile-nav" class="mobile-drawer" aria-label="Menú principal">
          <a routerLink="/" fragment="top" class="mobile-drawer-link" (click)="closeMobileNav()">Home</a>
          <a routerLink="/" fragment="nosotros" class="mobile-drawer-link" (click)="closeMobileNav()">Nosotros</a>
          <a routerLink="/" fragment="planes" class="mobile-drawer-link" (click)="closeMobileNav()">Planes</a>
          <a routerLink="/" fragment="contacto" class="mobile-drawer-link" (click)="closeMobileNav()">Contacto</a>
          <div class="mobile-drawer-divider"></div>
          <a routerLink="/auth/login" class="jcs-btn jcs-btn--ghost" (click)="closeMobileNav()">Login</a>
          @if (auth.isAuthenticated()) {
            <a routerLink="/app/dashboard" class="jcs-btn jcs-btn--primary" (click)="closeMobileNav()">Dashboard</a>
          } @else {
            <a routerLink="/auth/register" class="jcs-btn jcs-btn--primary" (click)="closeMobileNav()">Registrarse</a>
          }
        </nav>
      }
    </header>

    <!-- ============== Hero ============== -->
    <section id="top" class="hero">
      <div class="jcs-container hero-grid">
        <div class="hero-copy">
          <h1 class="hero-title">
            Convierte cada<br>
            operacion en una<br>
            decision <span class="hero-accent">mas inteligente</span>
          </h1>
          <p class="hero-lede">
            Registra tus operaciones de Forex y binarias, controla los saldos
            de tus cuentas y analiza tu P&amp;L para operar con mas claridad,
            confianza y disciplina.
          </p>
          <div class="hero-cta">
            <a routerLink="/auth/register" class="jcs-btn jcs-btn--primary jcs-btn--lg">Comenzar prueba gratuita</a>
            <a routerLink="/" fragment="planes" class="jcs-btn jcs-btn--ghost jcs-btn--lg">Ver planes</a>
          </div>
          <ul class="hero-bullets">
            <li><span class="check">&#10003;</span> Sin tarjeta de credito</li>
            <li><span class="check">&#10003;</span> Prueba 14 dias gratis</li>
            <li><span class="check">&#10003;</span> Cancela cuando quieras</li>
          </ul>
        </div>

        <aside class="hero-dash" aria-label="Preview del dashboard">
          <header class="dash-bar">
            <span class="dash-title">Resumen <span class="jcs-muted">/ Todas las cuentas</span></span>
            <span class="dash-period">20 may. - 16 jun. 2024</span>
          </header>
          <div class="dash-body">
            <div class="dash-stats">
              <div class="stat">
                <span class="stat-label">BALANCE TOTAL</span>
                <span class="stat-value jcs-num">$24,650.75</span>
                <span class="stat-foot jcs-pos">+8.42% desde el periodo anterior</span>
              </div>
              <div class="stat">
                <span class="stat-label">OPERACIONES</span>
                <span class="stat-value jcs-num">324</span>
                <span class="stat-foot jcs-pos">+15.2%</span>
              </div>
              <div class="stat stat--strong">
                <span class="stat-label">P&amp;L NETO</span>
                <span class="stat-value jcs-pos jcs-num">$2,186.45</span>
                <span class="stat-foot jcs-pos">+12.4%</span>
              </div>
            </div>
            <div class="dash-chart">
              <svg viewBox="0 0 320 100" preserveAspectRatio="none" width="100%" height="100">
                <defs>
                  <linearGradient id="hg" x1="0" y1="0" x2="0" y2="1">
                    <stop offset="0" stop-color="#2FDB78" stop-opacity="0.35"/>
                    <stop offset="1" stop-color="#2FDB78" stop-opacity="0"/>
                  </linearGradient>
                </defs>
                <path d="M0 70 L20 60 L40 75 L60 50 L80 65 L100 40 L120 55 L140 30 L160 45 L180 25 L200 40 L220 20 L240 35 L260 15 L280 30 L300 10 L320 25 L320 100 L0 100 Z" fill="url(#hg)"/>
                <path d="M0 70 L20 60 L40 75 L60 50 L80 65 L100 40 L120 55 L140 30 L160 45 L180 25 L200 40 L220 20 L240 35 L260 15 L280 30 L300 10 L320 25" fill="none" stroke="#2FDB78" stroke-width="2"/>
              </svg>
              <div class="chart-axis">
                <span>20-may</span><span>3-jun</span><span>10-jun</span><span>16-jun</span>
              </div>
            </div>
            <div class="dash-secondary">
              <div class="stat-small">
                <span class="stat-label">WIN RATE</span>
                <div class="donut" style="--p:63">
                  <span class="donut-value jcs-num">63%</span>
                </div>
                <span class="stat-foot jcs-pos">+4.1%</span>
              </div>
              <div class="stat-small">
                <span class="stat-label">DISTRIBUCION P&amp;L</span>
                <div class="bars">
                  <div class="row"><span>Ganadoras</span><span class="jcs-pos jcs-num">204 (63%)</span></div>
                  <div class="row bar-row"><div class="track"><div class="fill pos" style="width:63%"></div></div></div>
                  <div class="row"><span>Perdedoras</span><span class="jcs-neg jcs-num">120 (37%)</span></div>
                  <div class="row bar-row"><div class="track"><div class="fill neg" style="width:37%"></div></div></div>
                  <div class="row"><span>Break-even</span><span class="jcs-muted jcs-num">0 (0%)</span></div>
                </div>
              </div>
              <div class="stat-small">
                <span class="stat-label">MEJORES HORAS</span>
                <div class="heatmap">
                  @for (row of heatmap; track $index) {
                    <div class="hm-row">
                      @for (cell of row; track $index) {
                        <span class="hm-cell" [class.on]="cell === 1"></span>
                      }
                    </div>
                  }
                </div>
                <div class="hm-axis"><span>00:00</span><span>06:00</span><span>12:00</span><span>18:00</span><span>23:00</span></div>
              </div>
            </div>
            <div class="dash-trades">
              <span class="dash-trades-title">OPERACIONES RECIENTES</span>
              <div class="trades-head">
                <span>ACTIVO</span><span>DIRECCION</span><span>TAMANO</span><span>RESULTADO</span><span>FECHA</span>
              </div>
              @for (t of recent; track t.id) {
                <div class="trades-row">
                  <span class="symbol">{{ t.sym }}</span>
                  <span class="jcs-muted">{{ t.dir }}</span>
                  <span class="jcs-num">{{ t.size }}</span>
                  <span class="jcs-num" [class]="t.pnl.startsWith('+') ? 'jcs-pos' : 'jcs-neg'">{{ t.pnl }}</span>
                  <span class="jcs-muted jcs-num">{{ t.date }}</span>
                </div>
              }
            </div>
          </div>
        </aside>
      </div>
    </section>

    <!-- ============== Features ============== -->
    <section class="jcs-section features">
      <div class="jcs-container">
        <div class="features-grid">
          @for (f of features; track f.title) {
            <article class="feature">
              <span class="feature-icon" aria-hidden="true">
                <svg viewBox="0 0 24 24" width="22" height="22" fill="none" stroke="currentColor" stroke-width="1.6" stroke-linecap="round" stroke-linejoin="round">
                  <path [attr.d]="f.iconPath"/>
                </svg>
              </span>
              <h3>{{ f.title }}</h3>
              <p>{{ f.body }}</p>
            </article>
          }
        </div>
      </div>
    </section>

    <!-- ============== Nosotros ============== -->
    <section id="nosotros" class="jcs-section nosotros">
      <div class="jcs-container nosotros-card">
        <div class="nosotros-photo" aria-hidden="true">
          <svg viewBox="0 0 200 160" preserveAspectRatio="xMidYMid slice">
            <defs>
              <linearGradient id="pg" x1="0" y1="0" x2="1" y2="1">
                <stop offset="0" stop-color="#2FDB78" stop-opacity="0.18"/>
                <stop offset="1" stop-color="#2FDB78" stop-opacity="0"/>
              </linearGradient>
            </defs>
            <rect width="200" height="160" fill="#17212B"/>
            <circle cx="100" cy="80" r="56" fill="url(#pg)"/>
            <path d="M30 130 L60 100 L85 115 L115 75 L145 105 L175 70" fill="none" stroke="#2FDB78" stroke-width="2.4" stroke-linecap="round" stroke-linejoin="round"/>
            <circle cx="30" cy="130" r="3" fill="#2FDB78"/>
            <circle cx="60" cy="100" r="3" fill="#2FDB78"/>
            <circle cx="85" cy="115" r="3" fill="#2FDB78"/>
            <circle cx="115" cy="75" r="3" fill="#2FDB78"/>
            <circle cx="145" cy="105" r="3" fill="#2FDB78"/>
            <circle cx="175" cy="70" r="3" fill="#2FDB78"/>
          </svg>
        </div>
        <div class="nosotros-text">
          <h2>Nosotros</h2>
          <p>
            Trading Journal Pro nacio de la necesidad real de los traders de
            tener claridad y disciplina. Hemos estado en tu lugar: buenas rachas,
            malas decisiones y cuentas que no suman el esfuerzo real. Creamos esta
            plataforma para que tomes el control de tu operativa, entiendas tu
            rendimiento y construyas consistencia a largo plazo.
          </p>
          <p class="quote"><span class="heart">&#9829;</span> Hecho por traders, para traders.</p>
        </div>
        <div class="nosotros-stats">
          <div class="ns"><span class="ns-v jcs-pos jcs-num">+15K</span><span class="ns-l">Traders activos</span></div>
          <div class="ns"><span class="ns-v jcs-pos jcs-num">+1.2M</span><span class="ns-l">Operaciones registradas</span></div>
          <div class="ns"><span class="ns-v jcs-pos jcs-num">90+</span><span class="ns-l">Paises</span></div>
          <div class="ns"><span class="ns-v jcs-pos jcs-num">4.9/5</span><span class="ns-l">Valoracion promedio</span></div>
        </div>
      </div>
    </section>

    <!-- ============== Planes ============== -->
    <section id="planes" class="jcs-section planes">
      <div class="jcs-container">
        <header class="section-head section-head--center">
          <h2>Elige el plan que se adapta a tu operativa</h2>
        </header>
        <div class="billing-toggle" role="group" aria-label="Frecuencia de facturación">
          <button
            type="button"
            class="toggle"
            [class.on]="!annual()"
            [attr.aria-pressed]="!annual()"
            (click)="annual.set(false)">Mensual</button>
          <button
            type="button"
            class="toggle"
            [class.on]="annual()"
            [attr.aria-pressed]="annual()"
            (click)="annual.set(true)">Anual <span class="save">-20%</span></button>
          <span class="hint" role="presentation">Ahorra 2 meses con el plan anual</span>
        </div>
        <div class="plans-grid">
          @for (p of plans(); track p.code) {
            <article class="plan" [class.plan--highlight]="p.highlight">
              @if (p.highlight) { <span class="plan-flag">MÁS ELEGIDO</span> }
              <header class="plan-head">
                <h3 class="plan-name" [class.plan-name--green]="p.highlight">{{ p.name }}</h3>
                <p class="plan-blurb">{{ p.blurb }}</p>
                <div class="plan-price">
                  <span class="plan-currency">$</span>
                  <span class="plan-amount jcs-num">{{ annual() ? p.annual : p.monthly }}</span>
                  <span class="plan-period">/ mes</span>
                </div>
                <p class="plan-billed jcs-muted">Facturado {{ annual() ? 'anualmente' : 'mensualmente' }}</p>
              </header>
              <ul class="plan-features">
                @for (f of p.features; track f) { <li><span class="ck">&#10003;</span>{{ f }}</li> }
              </ul>
              <a routerLink="/auth/register" class="jcs-btn jcs-btn--block" [class.jcs-btn--primary]="p.highlight" [class.jcs-btn--ghost]="!p.highlight">Comenzar ahora</a>
            </article>
          }
        </div>
      </div>
    </section>

    <!-- ============== FAQ ============== -->
    <section id="contacto" class="jcs-section faq">
      <div class="jcs-container faq-wrap">
        <header class="section-head section-head--center">
          <h2>¿Tienes preguntas sobre la plataforma?</h2>
        </header>
        <div class="faq-grid">
          <div class="faq-contact">
            <a class="faq-channel">
              <span class="faq-icon">
                <svg viewBox="0 0 24 24" width="20" height="20" fill="none" stroke="currentColor" stroke-width="1.6" stroke-linecap="round" stroke-linejoin="round"><rect x="3" y="5" width="18" height="14" rx="2"/><path d="M3 7l9 6 9-6"/></svg>
              </span>
              <div>
                <span class="faq-ch-title">Email</span>
                <span class="faq-ch-line">hola&#64;jadecapitalsuite.com</span>
                <span class="faq-ch-foot jcs-muted">Respondemos en 24h</span>
              </div>
            </a>
            <a class="faq-channel">
              <span class="faq-icon">
                <svg viewBox="0 0 24 24" width="20" height="20" fill="none" stroke="currentColor" stroke-width="1.6" stroke-linecap="round" stroke-linejoin="round"><path d="M22 16.92v3a2 2 0 01-2.18 2 19.79 19.79 0 01-8.63-3.07 19.5 19.5 0 01-6-6A19.79 19.79 0 012.12 4.18 2 2 0 014.11 2h3a2 2 0 012 1.72 12.84 12.84 0 00.7 2.81 2 2 0 01-.45 2.11L8.09 9.91a16 16 0 006 6l1.27-1.27a2 2 0 012.11-.45 12.84 12.84 0 002.81.7A2 2 0 0122 16.92z"/></svg>
              </span>
              <div>
                <span class="faq-ch-title">Telefono</span>
                <span class="faq-ch-line">+34 911 23 45 67</span>
                <span class="faq-ch-foot jcs-muted">Lun - Vie de 09:00 a 18:00</span>
              </div>
            </a>
            <a class="faq-channel">
              <span class="faq-icon">
                <svg viewBox="0 0 24 24" width="20" height="20" fill="none" stroke="currentColor" stroke-width="1.6" stroke-linecap="round" stroke-linejoin="round"><path d="M21 11.5a8.38 8.38 0 01-.9 3.8 8.5 8.5 0 01-7.6 4.7 8.38 8.38 0 01-3.8-.9L3 21l1.9-5.7a8.38 8.38 0 01-.9-3.8 8.5 8.5 0 014.7-7.6 8.38 8.38 0 013.8-.9h.5a8.48 8.48 0 018 8v.5z"/></svg>
              </span>
              <div>
                <span class="faq-ch-title">WhatsApp</span>
                <span class="faq-ch-line">+34 644 12 34 56</span>
                <span class="faq-ch-foot jcs-muted">Respuesta rápida</span>
              </div>
            </a>
          </div>
          <div class="faq-list">
            @for (item of faqs; track item.q) {
              <details class="faq-item">
                <summary>{{ item.q }}<span class="faq-toggle"></span></summary>
                <p>{{ item.a }}</p>
              </details>
            }
          </div>
        </div>
      </div>
    </section>

    <!-- ============== Footer ============== -->
    <footer class="jcs-footer">
      <div class="jcs-container footer-grid">
        <div class="footer-brand">
          <a class="brand" routerLink="/">
            <span class="brand-mark" aria-hidden="true">
              <svg viewBox="0 0 24 24" width="22" height="22" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
                <path d="M3 3v18h18"/><path d="M7 14l4-4 3 3 5-6"/>
              </svg>
            </span>
            <span class="brand-name">JadeCapital<strong>Suite</strong> <span class="jcs-badge jcs-badge--neutral brand-tag">PRO</span></span>
          </a>
          <p class="footer-tag">Convierte tus datos en decisiones.<br>Mejora tu disciplina. Crece como trader.</p>
        </div>
        <div class="footer-col">
          <h4>Producto</h4>
          <a href="#features">Caracteristicas</a>
          <a href="#nosotros">Nosotros</a>
          <a href="#planes">Planes</a>
          <a href="#top">Actualizaciones</a>
        </div>
        <div class="footer-col">
          <h4>Compañia</h4>
          <a href="#nosotros">Nosotros</a>
          <a href="#contacto">Contacto</a>
          <a href="#">Privacidad</a>
        </div>
        <div class="footer-col">
          <h4>Recursos</h4>
          <a href="#">Blog</a>
          <a href="#">Guias</a>
          <a href="#">Centro de ayuda</a>
        </div>
        <div class="footer-col footer-newsletter">
          <h4>Recibe tips y actualizaciones</h4>
          <form class="newsletter" (submit)="$event.preventDefault()">
            <input type="email" class="jcs-input" placeholder="Tu email"/>
            <button type="submit" class="jcs-btn jcs-btn--primary">Suscribirme</button>
          </form>
          <div class="socials">
            <a aria-label="YouTube"><svg viewBox="0 0 24 24" width="18" height="18" fill="currentColor"><path d="M23 12s0-3.6-.5-5.3a3 3 0 00-2.1-2.1C18.6 4 12 4 12 4s-6.6 0-8.4.6A3 3 0 001.5 6.7C1 8.4 1 12 1 12s0 3.6.5 5.3a3 3 0 002.1 2.1C5.4 20 12 20 12 20s6.6 0 8.4-.6a3 3 0 002.1-2.1C23 15.6 23 12 23 12zM9.7 15V9l5.7 3-5.7 3z"/></svg></a>
            <a aria-label="Twitter"><svg viewBox="0 0 24 24" width="18" height="18" fill="currentColor"><path d="M22 5.8a8.4 8.4 0 01-2.36.64 4.13 4.13 0 001.81-2.27 8.18 8.18 0 01-2.6 1 4.1 4.1 0 00-7 3.74 11.65 11.65 0 01-8.45-4.29 4.1 4.1 0 001.27 5.49 4.05 4.05 0 01-1.86-.51v.05a4.1 4.1 0 003.3 4 4.1 4.1 0 01-1.85.07 4.1 4.1 0 003.83 3 4.1 4.1 0 01-6.05 1.7A11 11 0 0010 18.5a11 11 0 01-6-.17 14.3 14.3 0 009.8 2.32c8.86 0 13.71-7.34 13.71-13.71 0-.21 0-.42-.01-.62A9.78 9.78 0 0024 4.54a8.5 8.5 0 01-2 .56z"/></svg></a>
            <a aria-label="GitHub"><svg viewBox="0 0 24 24" width="18" height="18" fill="currentColor"><path d="M12 .5a11.5 11.5 0 00-3.63 22.4c.58.1.79-.25.79-.56v-2c-3.2.7-3.88-1.36-3.88-1.36-.52-1.33-1.28-1.69-1.28-1.69-1.05-.72.08-.7.08-.7 1.16.08 1.78 1.2 1.78 1.2 1.03 1.78 2.7 1.27 3.36.97.1-.75.4-1.27.73-1.56-2.55-.3-5.24-1.28-5.24-5.7a4.47 4.47 0 011.18-3.1 4.15 4.15 0 01.11-3.06s.97-.31 3.18 1.18a11 11 0 015.78 0c2.2-1.5 3.17-1.18 3.17-1.18a4.13 4.13 0 01.12 3.05 4.46 4.46 0 011.18 3.1c0 4.44-1.7 5.4-3.26 5.7.4.36.74 1.05.74 2.13v3.16c0 .31.2.67.8.55A11.5 11.5 0 0012 .5z"/></svg></a>
          </div>
        </div>
      </div>
      <div class="jcs-container footer-bottom">
        <p class="jcs-soft">JadeCapital Suite Pro no proporciona asesoramiento financiero ni de inversion. La plataforma es una herramienta de registro y análisis. El trading involucra riesgos. Toma decisiones basadas en datos, no en emociones.</p>
        <p class="jcs-muted footer-copy">&copy; 2024 JadeCapital Suite. Todos los derechos reservados.</p>
      </div>
    </footer>
  `,
  styles: [`
    /* ============== Top bar ============== */
    .topbar { position: sticky; top: 0; z-index: 30; background: rgba(5,11,16,0.85); backdrop-filter: blur(14px); border-bottom: 1px solid var(--border-soft); }
    .nav { display: flex; align-items: center; justify-content: space-between; height: 64px; gap: var(--sp-6); }
    .brand { display: inline-flex; align-items: center; gap: 10px; color: var(--text-main); }
    .brand-mark { width: 28px; height: 28px; border-radius: 6px; background: var(--bg-elevated); color: var(--green); display: inline-flex; align-items: center; justify-content: center; }
    .brand-name { font-weight: 400; font-size: var(--fs-sm); letter-spacing: 0.02em; display: inline-flex; align-items: center; gap: 6px; }
    .brand-name strong { font-weight: 700; }
    .brand-tag { font-size: 9px; padding: 1px 5px; }
    .nav-links { display: flex; gap: var(--sp-6); flex: 1; justify-content: center; }
    .nav-links a { color: var(--text-muted); font-size: var(--fs-sm); padding: var(--sp-2) 0; position: relative; }
    .nav-links a:hover { color: var(--text-main); }
    .nav-links a.active { color: var(--green); }
    .nav-links a.active::after { content: ''; position: absolute; left: 50%; bottom: -2px; transform: translateX(-50%); width: 22px; height: 2px; background: var(--green); border-radius: 2px; }
    .nav-cta { display: flex; gap: var(--sp-2); align-items: center; }

    /* Hamburger button — hidden on tablet+. */
    .hamburger {
      display: none;
      width: 40px;
      height: 40px;
      align-items: center;
      justify-content: center;
      background: transparent;
      border: 1px solid var(--border);
      border-radius: var(--radius-sm);
      color: var(--text-main);
      cursor: pointer;
      padding: 0;
    }
    .hamburger:hover { background: var(--bg-hover); border-color: var(--border-active); }
    .hamburger:focus-visible { outline: 2px solid var(--border-active); outline-offset: 2px; }

    /* Mobile drawer (hamburger menu). */
    .mobile-drawer-backdrop {
      position: fixed;
      inset: 0;
      background: rgba(0,0,0,0.55);
      backdrop-filter: blur(4px);
      z-index: calc(var(--z-mobile-nav) - 1);
    }
    .mobile-drawer {
      position: fixed;
      top: 0;
      right: 0;
      bottom: 0;
      width: min(320px, 85vw);
      padding: var(--sp-16) var(--sp-6) var(--sp-6);
      background: var(--bg-sidebar);
      border-left: 1px solid var(--border);
      display: flex;
      flex-direction: column;
      gap: var(--sp-3);
      z-index: var(--z-mobile-nav);
      box-shadow: -8px 0 24px rgba(0,0,0,0.32);
      animation: drawer-slide-in 220ms cubic-bezier(0.2, 0.8, 0.2, 1);
    }
    @keyframes drawer-slide-in {
      from { transform: translateX(100%); opacity: 0; }
      to   { transform: translateX(0);    opacity: 1; }
    }
    .mobile-drawer-link {
      color: var(--text-main);
      text-decoration: none;
      padding: var(--sp-3) var(--sp-4);
      border-radius: var(--radius-sm);
      font-size: var(--fs-base);
      font-weight: 500;
      transition: background 150ms ease;
    }
    .mobile-drawer-link:hover { background: var(--bg-hover); }
    .mobile-drawer-divider {
      height: 1px;
      background: var(--border);
      margin: var(--sp-2) 0;
    }

    @media (max-width: 768px) {
      .nav-links { display: none; }
      .nav-cta-login { display: none; }
      .hamburger { display: inline-flex; }
    }
    @media (min-width: 769px) {
      .mobile-drawer,
      .mobile-drawer-backdrop { display: none !important; }
    }

    /* ============== Hero ============== */
    .hero { padding: var(--sp-16) 0 var(--sp-24); }
    .hero-grid { display: grid; grid-template-columns: 1fr 1.4fr; gap: var(--sp-8); align-items: start; }
    @media (max-width: 1024px) { .hero-grid { grid-template-columns: 1fr; } }
    .hero-title { font-size: clamp(2.4rem, 5vw, 4.2rem); font-weight: 800; line-height: 1.05; letter-spacing: -0.04em; margin: 0 0 var(--sp-6); }
    .hero-accent { color: var(--green); }
    .hero-lede { color: var(--text-muted); font-size: var(--fs-base); line-height: 1.55; max-width: 460px; margin: 0 0 var(--sp-8); }
    .hero-cta { display: flex; gap: var(--sp-3); flex-wrap: wrap; margin-bottom: var(--sp-6); }
    .hero-bullets { list-style: none; padding: 0; margin: 0; display: flex; gap: var(--sp-6); flex-wrap: wrap; font-size: var(--fs-sm); color: var(--text-muted); }
    .hero-bullets li { display: inline-flex; align-items: center; gap: 6px; }
    .check { color: var(--green); font-weight: 700; }

    /* ============== Dashboard preview (right) ============== */
    .hero-dash { background: var(--bg-card); border: 1px solid var(--border); border-radius: var(--radius-lg); overflow: hidden; box-shadow: var(--shadow-soft); }
    .dash-bar { display: flex; align-items: center; justify-content: space-between; padding: var(--sp-2) var(--sp-4); border-bottom: 1px solid var(--border-soft); background: rgba(255,255,255,0.02); }
    .dash-title { font-size: var(--fs-sm); font-weight: 600; }
    .dash-period { font-size: var(--fs-xs); color: var(--text-soft); }
    .dash-body { padding: var(--sp-3) var(--sp-4); display: flex; flex-direction: column; gap: var(--sp-3); }
    .dash-stats { display: grid; grid-template-columns: 1fr 1fr 1fr; gap: var(--sp-2); }
    .stat { background: var(--bg-card-soft); border: 1px solid var(--border-soft); border-radius: var(--radius-md); padding: var(--sp-2) var(--sp-3); display: flex; flex-direction: column; gap: 2px; }
    .stat--strong { background: linear-gradient(180deg, rgba(47,219,120,0.08) 0%, var(--bg-card-soft) 100%); border-color: rgba(47,219,120,0.25); }
    .stat-label { font-size: 9px; color: var(--text-muted); text-transform: uppercase; letter-spacing: 0.08em; }
    .stat-value { font-size: var(--fs-lg); font-weight: 700; letter-spacing: -0.02em; line-height: 1.1; }
    .stat-foot { font-size: 10px; }
    .dash-chart { display: flex; flex-direction: column; gap: 4px; }
    .dash-chart svg { display: block; height: 64px; }
    .chart-axis { display: flex; justify-content: space-between; font-size: 9px; color: var(--text-soft); }
    .dash-secondary { display: grid; grid-template-columns: 1fr 1.4fr 1.4fr; gap: var(--sp-2); }
    .stat-small { background: var(--bg-card-soft); border: 1px solid var(--border-soft); border-radius: var(--radius-md); padding: var(--sp-2) var(--sp-3); display: flex; flex-direction: column; gap: 4px; }
    .donut { width: 56px; height: 56px; border-radius: 50%; background: conic-gradient(var(--green) calc(var(--p) * 1%), var(--bg-elevated) 0); display: flex; align-items: center; justify-content: center; margin: 4px auto; position: relative; }
    .donut::after { content: ''; position: absolute; inset: 6px; border-radius: 50%; background: var(--bg-card-soft); }
    .donut-value { position: relative; z-index: 1; font-size: var(--fs-base); font-weight: 700; }
    .bars { display: flex; flex-direction: column; gap: 3px; }
    .row { display: flex; justify-content: space-between; font-size: 10px; }
    .bar-row { height: 6px; }
    .track { background: var(--bg-elevated); border-radius: 3px; overflow: hidden; }
    .fill { height: 100%; border-radius: 3px; }
    .fill.pos { background: var(--green); }
    .fill.neg { background: var(--red); }
    .heatmap { display: flex; flex-direction: column; gap: 2px; }
    .hm-row { display: grid; grid-template-columns: repeat(7, 1fr); gap: 2px; }
    .hm-cell { aspect-ratio: 1; background: var(--bg-elevated); border-radius: 2px; }
    .hm-cell.on { background: var(--green); opacity: 0.7; }
    .hm-axis { display: flex; justify-content: space-between; font-size: 9px; color: var(--text-soft); margin-top: 4px; }
    .dash-trades { background: var(--bg-card-soft); border: 1px solid var(--border-soft); border-radius: var(--radius-md); padding: var(--sp-2) var(--sp-3); }
    .dash-trades-title { display: block; font-size: 9px; color: var(--text-muted); text-transform: uppercase; letter-spacing: 0.08em; margin-bottom: var(--sp-2); }
    .trades-head, .trades-row { display: grid; grid-template-columns: 1.5fr 1fr 1fr 1fr 1.2fr; gap: var(--sp-2); font-size: 10px; padding: 4px 0; align-items: center; }
    .trades-head { color: var(--text-muted); text-transform: uppercase; letter-spacing: 0.05em; }
    .trades-row + .trades-row { border-top: 1px solid var(--border-soft); }
    .symbol { font-weight: 600; color: var(--text-main); }
    @media (max-width: 540px) { .dash-stats, .dash-secondary { grid-template-columns: 1fr; } }

    /* ============== Features ============== */
    .features { background: transparent; }
    .features-grid { display: grid; grid-template-columns: repeat(4, 1fr); gap: var(--sp-4); }
    @media (max-width: 1024px) { .features-grid { grid-template-columns: repeat(2, 1fr); } }
    @media (max-width: 540px) { .features-grid { grid-template-columns: 1fr; } }
    .feature { background: var(--bg-card); border: 1px solid var(--border); border-radius: var(--radius-md); padding: var(--sp-6); display: flex; flex-direction: column; gap: var(--sp-3); }
    .feature-icon { width: 36px; height: 36px; border-radius: var(--radius-sm); background: var(--green-soft); color: var(--green); display: inline-flex; align-items: center; justify-content: center; }
    .feature h3 { font-size: var(--fs-base); font-weight: 600; }
    .feature p { color: var(--text-muted); font-size: var(--fs-sm); line-height: 1.55; margin: 0; }

    /* ============== Nosotros ============== */
    .nosotros-card { background: var(--bg-card); border: 1px solid var(--border); border-radius: var(--radius-lg); padding: var(--sp-8); display: grid; grid-template-columns: 180px 1fr 1fr; gap: var(--sp-8); align-items: center; }
    @media (max-width: 900px) { .nosotros-card { grid-template-columns: 1fr; } }
    .nosotros-photo { border-radius: var(--radius-md); overflow: hidden; }
    .nosotros-text h2 { font-size: var(--fs-2xl); margin-bottom: var(--sp-3); }
    .nosotros-text p { color: var(--text-muted); font-size: var(--fs-sm); line-height: 1.6; margin: 0 0 var(--sp-3); }
    .quote { font-style: italic; color: var(--green); }
    .heart { color: var(--green); margin-right: 4px; }
    .nosotros-stats { display: grid; grid-template-columns: 1fr 1fr; gap: var(--sp-5); }
    .ns { display: flex; flex-direction: column; gap: 2px; }
    .ns-v { font-size: var(--fs-2xl); font-weight: 700; }
    .ns-l { font-size: var(--fs-sm); color: var(--text-muted); }

    /* ============== Planes ============== */
    .section-head h2 { font-size: clamp(1.75rem, 3vw, 2.25rem); font-weight: 700; letter-spacing: -0.03em; margin: 0 0 var(--sp-4); }
    .section-head--center { margin: 0 auto var(--sp-8); text-align: center; }
    .billing-toggle { display: inline-flex; align-items: center; gap: var(--sp-2); padding: 4px; background: var(--bg-card); border: 1px solid var(--border); border-radius: var(--radius-pill, 9999px); margin: 0 auto var(--sp-6); position: relative; }
    .toggle { padding: var(--sp-2) var(--sp-5); border-radius: 9999px; border: 0; background: transparent; color: var(--text-muted); font: inherit; font-size: var(--fs-sm); font-weight: 600; cursor: pointer; transition: all 200ms; }
    .toggle.on { background: var(--green); color: #050B10; }
    .save { font-size: 10px; background: rgba(47,219,120,0.15); color: var(--green); padding: 1px 6px; border-radius: 4px; margin-left: 4px; }
    .hint { color: var(--text-muted); font-size: var(--fs-sm); display: block; text-align: center; margin-top: var(--sp-3); }
    .plans-grid { display: grid; grid-template-columns: repeat(3, 1fr); gap: var(--sp-4); }
    @media (max-width: 900px) { .plans-grid { grid-template-columns: 1fr; } }
    .plan { background: var(--bg-card); border: 1px solid var(--border); border-radius: var(--radius-md); padding: var(--sp-6); display: flex; flex-direction: column; gap: var(--sp-5); position: relative; }
    .plan--highlight { border-color: var(--border-active); background: linear-gradient(180deg, rgba(47,219,120,0.04) 0%, var(--bg-card) 100%); }
    .plan-flag { position: absolute; top: -10px; left: 50%; transform: translateX(-50%); background: var(--green); color: #050B10; font-size: 9px; font-weight: 700; padding: 3px 10px; border-radius: 9999px; letter-spacing: 0.06em; }
    .plan-head { display: flex; flex-direction: column; gap: 6px; }
    .plan-name { font-size: var(--fs-base); font-weight: 600; color: var(--text-muted); }
    .plan-name--green { color: var(--green); }
    .plan-blurb { font-size: var(--fs-sm); color: var(--text-muted); margin: 0 0 var(--sp-3); }
    .plan-price { display: flex; align-items: baseline; gap: 4px; }
    .plan-currency { font-size: var(--fs-lg); color: var(--text-muted); }
    .plan-amount { font-size: var(--fs-4xl); font-weight: 700; letter-spacing: -0.03em; }
    .plan-period { color: var(--text-muted); font-size: var(--fs-sm); }
    .plan-billed { font-size: var(--fs-xs); margin: 0; }
    .plan-features { list-style: none; padding: 0; margin: 0; display: flex; flex-direction: column; gap: var(--sp-2); }
    .plan-features li { display: flex; gap: 8px; font-size: var(--fs-sm); color: var(--text-secondary); }
    .ck { color: var(--green); font-weight: 700; }

    /* ============== FAQ ============== */
    .faq-wrap { max-width: 980px; margin: 0 auto; }
    .faq-grid { display: grid; grid-template-columns: 1fr 1.4fr; gap: var(--sp-8); }
    @media (max-width: 768px) { .faq-grid { grid-template-columns: 1fr; } }
    .faq-contact { display: flex; flex-direction: column; gap: var(--sp-4); }
    .faq-channel { display: flex; align-items: center; gap: var(--sp-3); padding: var(--sp-3); background: var(--bg-card); border: 1px solid var(--border); border-radius: var(--radius-md); }
    .faq-icon { width: 36px; height: 36px; border-radius: 50%; background: var(--green-soft); color: var(--green); display: inline-flex; align-items: center; justify-content: center; flex-shrink: 0; }
    .faq-ch-title { display: block; font-size: var(--fs-xs); color: var(--text-muted); text-transform: uppercase; letter-spacing: 0.08em; }
    .faq-ch-line { display: block; font-size: var(--fs-sm); color: var(--text-main); font-weight: 600; }
    .faq-ch-foot { display: block; font-size: var(--fs-xs); }
    .faq-list { display: flex; flex-direction: column; }
    .faq-item { border-bottom: 1px solid var(--border-soft); padding: var(--sp-4) 0; }
    .faq-item summary { display: flex; align-items: center; justify-content: space-between; cursor: pointer; font-size: var(--fs-sm); font-weight: 500; list-style: none; }
    .faq-item summary::-webkit-details-marker { display: none; }
    .faq-toggle { width: 12px; height: 12px; position: relative; flex-shrink: 0; }
    .faq-toggle::before, .faq-toggle::after { content: ''; position: absolute; background: var(--text-muted); }
    .faq-toggle::before { width: 10px; height: 1.5px; top: 5px; left: 1px; }
    .faq-toggle::after { width: 1.5px; height: 10px; top: 1px; left: 5px; transition: transform 200ms; }
    .faq-item[open] .faq-toggle::after { transform: scaleY(0); }
    .faq-item p { color: var(--text-muted); margin: var(--sp-3) 0 0; line-height: 1.6; font-size: var(--fs-sm); }

    /* ============== Footer ============== */
    .jcs-footer { border-top: 1px solid var(--border-soft); padding: var(--sp-12) 0 var(--sp-6); margin-top: var(--sp-12); background: var(--bg-card-soft); }
    .footer-grid { display: grid; grid-template-columns: 1.5fr 1fr 1fr 1fr 1.5fr; gap: var(--sp-8); padding-bottom: var(--sp-8); }
    @media (max-width: 1024px) { .footer-grid { grid-template-columns: 1fr 1fr 1fr; } }
    @media (max-width: 540px) { .footer-grid { grid-template-columns: 1fr 1fr; } }
    .footer-brand .brand { margin-bottom: var(--sp-3); }
    .footer-tag { font-size: var(--fs-sm); color: var(--text-muted); margin: 0; line-height: 1.5; }
    .footer-col { display: flex; flex-direction: column; gap: var(--sp-2); }
    .footer-col h4 { font-size: var(--fs-sm); margin: 0 0 var(--sp-2); }
    .footer-col a { font-size: var(--fs-sm); color: var(--text-muted); }
    .footer-col a:hover { color: var(--text-main); }
    .newsletter { display: flex; gap: var(--sp-2); }
    .newsletter .jcs-input { padding: var(--sp-2) var(--sp-3); font-size: var(--fs-xs); }
    .newsletter .jcs-btn { padding: var(--sp-2) var(--sp-4); font-size: var(--fs-xs); }
    .socials { display: flex; gap: var(--sp-3); margin-top: var(--sp-3); }
    .socials a { width: 32px; height: 32px; border-radius: 50%; background: var(--bg-elevated); color: var(--text-muted); display: inline-flex; align-items: center; justify-content: center; }
    .socials a:hover { color: var(--green); }
    .footer-bottom { border-top: 1px solid var(--border-soft); padding-top: var(--sp-4); margin-top: var(--sp-4); display: flex; justify-content: space-between; gap: var(--sp-4); flex-wrap: wrap; }
    .footer-copy { font-size: var(--fs-xs); }
  `],
})
export class LandingPage {
  /** Mobile hamburger drawer state. */
  readonly mobileNavOpen = signal(false);

  toggleMobileNav(): void {
    this.mobileNavOpen.update(v => !v);
  }

  closeMobileNav(): void {
    this.mobileNavOpen.set(false);
  }
  readonly auth = inject(AuthState);
  private readonly api = inject(PlanApiService);
  private readonly destroyRef = inject(DestroyRef);
  readonly annual = signal(false);
  readonly plans = signal<readonly Plan[]>([]);

  readonly features: readonly Feature[] = [
    { title: 'Controla tus cuentas', body: 'Gestiona multiples cuentas, visualiza saldos en tiempo real y sigue la evolucion de tu capital.', iconPath: 'M3 7h18M3 12h18M3 17h18' },
    { title: 'Registra cada operacion', body: 'Registra Forex y binarias en segundos. Incluye entradas, salidas, tamano, activos y notas.', iconPath: 'M12 4v16m8-8H4' },
    { title: 'Analiza tu rendimiento', body: 'Metricas avanzadas, graficos interactivos y reportes claros para encontrar tu ventaja.', iconPath: 'M3 3v18h18M7 14l4-4 3 3 5-6' },
    { title: 'Mejora tu disciplina', body: 'Detecta patrones, controla el riesgo y toma decisiones basadas en datos, no en emociones.', iconPath: 'M12 2l3 7h7l-7 3 3 7-3-7-7 3 7-7-3 3-7 7z' },
  ];

  constructor() {
    from(this.api.list())
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe(api => this.plans.set(this.mergeWithMarketing(api)));
  }

  private mergeWithMarketing(api: readonly PlanInfo[]): readonly Plan[] {
    return api.map(p => {
      const meta = PLAN_MARKETING[p.code] ?? { features: [], blurb: '', highlight: false, annualDiscount: 0.20 };
      const monthly = p.monthlyPrice;
      const annual = Math.round(monthly * (1 - meta.annualDiscount));
      return {
        code: p.code,
        name: p.name,
        monthly,
        annual,
        blurb: meta.blurb,
        features: meta.features,
        highlight: meta.highlight,
      };
    });
  }

  readonly faqs: readonly Faq[] = [
    { q: '¿Que tipos de operaciones puedo registrar?', a: 'Forex, binarias, indices, cripto y futuros. Cada tipo con sus campos especificos.' },
    { q: '¿Puedo usar el plan en mas de un dispositivo?', a: 'Si, tu cuenta se sincroniza automaticamente entre todos los dispositivos donde ingreses.' },
    { q: '¿Mis datos estan seguros?', a: 'Si. Usamos encriptacion AES-256, autenticacion JWT con rotacion de tokens y backups diarios.' },
    { q: '¿Puedo cancelar mi suscripcion cuando quiera?', a: 'Si, sin preguntas. Mantienes acceso hasta el final del periodo pagado y los datos quedan disponibles 30 dias mas.' },
  ];

  // Heatmap: 7 rows (days) x 7 cols (4-hour windows). 1 = active hour.
  readonly heatmap: readonly number[][] = [
    [0,0,0,1,1,1,0],
    [0,0,1,1,1,1,0],
    [0,0,1,1,1,0,0],
    [0,0,0,1,1,1,0],
    [0,0,1,1,1,1,0],
    [0,0,0,1,1,0,0],
    [0,0,0,0,1,0,0],
  ];

  readonly recent = [
    { id: 1, sym: 'EUR/USD', dir: 'Compra', size: '0.50', pnl: '+$82.45', date: '16 jun. 14:32' },
    { id: 2, sym: 'GBP/JPY', dir: 'Venta',  size: '0.30', pnl: '+$58.20', date: '16 jun. 11:07' },
    { id: 3, sym: 'AUD/USD', dir: 'Compra', size: '0.40', pnl: '-$43.10', date: '16 jun. 09:21' },
    { id: 4, sym: 'USD/JPY', dir: 'Compra', size: '1.00', pnl: '-$58.00', date: '15 jun. 19:43' },
  ];
}
