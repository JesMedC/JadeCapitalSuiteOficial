import { ChangeDetectionStrategy, Component } from '@angular/core';
import { RouterLink } from '@angular/router';

// ============================================================================
//  PrivacyPolicyPage — Wave 11 slice 11.4.
//
//  Lazy-loaded from /legal/privacy. Same WARNING banner contract as the ToS
//  page: PLACEHOLDER copy until legal counsel approves the canonical
//  version. The page renders the GDPR Art. 13 information notice (categories
//  of data + legal basis + retention + rights) inline so the FE can ship
//  even before the canonical Markdown lands in
//  <c>frontend/src/assets/legal/privacy-policy.md</c>.
// ============================================================================

@Component({
  selector: 'jcs-privacy-policy',
  standalone: true,
  imports: [RouterLink],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <section class="legal-page" data-testid="privacy-policy-page">
      <header class="legal-warning" data-testid="legal-warning-banner" role="alert">
        <span class="legal-warning__icon" aria-hidden="true">⚠️</span>
        <div class="legal-warning__body">
          <strong>DO NOT DEPLOY WITHOUT LEGAL COUNSEL SIGN-OFF.</strong>
          El texto es PLACEHOLDER generado para desarrollo. Sustituir
          por la versión canónica aprobada por legal + producto antes
          del lanzamiento v1.0 GA.
        </div>
      </header>

      <article class="legal-document">
        <h1>Política de Privacidad — Jade Capital Suite</h1>
        <p class="legal-version">
          Versión: placeholder (pre-v1.0) · Pendiente de revisión legal
        </p>

        <h2>1. Quiénes somos</h2>
        <p>
          Jade Capital Suite ("nosotros") opera la plataforma de
          registro + análisis de trading accesible desde el dominio de
          Jade Capital Suite. Somos el responsable del tratamiento de
          los datos personales recolectados a través del Servicio.
        </p>

        <h2>2. Qué datos recolectamos</h2>
        <ul>
          <li>Cuenta: email, nombre, hash de contraseña.</li>
          <li>Consentimientos: timestamps + IP al aceptar ToS y cookies (Art. 7 RGPD).</li>
          <li>Tenant: nombre y slug de la organización.</li>
          <li>Uso: features accedidos, reportes de error anonimizados.</li>
        </ul>

        <h2>3. Por qué los recolectamos (base legal)</h2>
        <ul>
          <li><strong>Ejecución del contrato</strong> (Art. 6.1.b RGPD): prestar el Servicio.</li>
          <li><strong>Interés legítimo</strong> (Art. 6.1.f RGPD): seguridad + prevención de abuso.</li>
          <li><strong>Consentimiento</strong> (Art. 6.1.a RGPD): cookies no esenciales + ledger de aceptación.</li>
        </ul>

        <h2>4. Plazo de conservación</h2>
        <p>
          Conservamos los datos mientras tu cuenta esté activa. Tras la
          eliminación (<code>DELETE /api/users/me/account</code>), los
          datos entran en un período de gracia de 30 días antes de su
          anonimización irreversible + seudonimización del rastro de
          auditoría.
        </p>

        <h2>5. Tus derechos (RGPD Art. 15-22)</h2>
        <ul>
          <li><strong>Acceso</strong>: <code>GET /api/users/me/export</code>.</li>
          <li><strong>Rectificación</strong>: ajustes de la cuenta.</li>
          <li><strong>Supresión</strong>: <code>DELETE /api/users/me/account</code>.</li>
          <li><strong>Oposición</strong> y <strong>portabilidad</strong>: contactanos.</li>
        </ul>

        <h2>6. Cookies</h2>
        <p>
          El banner de cookies predeterminado es <strong>solo
          esenciales</strong>. Podés optar por cookies analíticas con
          "Aceptar todas". NO instalamos cookies no esenciales antes
          del consentimiento (Directiva ePrivacy 2002/58/CE art. 5.3).
        </p>

        <h2>7. Contacto</h2>
        <p>
          Consultas sobre privacidad:
          <a href="mailto:privacy&#64;jadecapital.com">privacy&#64;jadecapital.com</a>.
        </p>
      </article>

      <footer class="legal-foot">
        <a routerLink="/legal/terms">← Términos de servicio</a>
        <a routerLink="/">Volver al inicio</a>
      </footer>
    </section>
  `,
  styles: [`
    .legal-page {
      max-width: 760px;
      margin: 0 auto;
      padding: var(--jcs-space-12, 3rem) var(--jcs-space-6, 1.5rem) var(--jcs-space-12, 3rem);
    }

    .legal-warning {
      display: flex;
      gap: var(--jcs-space-3, 0.75rem);
      align-items: flex-start;
      padding: var(--jcs-space-4, 1rem);
      border: 1px solid #f59e0b;
      background: #fff7e6;
      border-radius: var(--jcs-radius-md, 8px);
      margin-bottom: var(--jcs-space-6, 1.5rem);
      color: #7c2d12;
    }

    .legal-warning__icon {
      font-size: 1.5rem;
      line-height: 1;
      flex-shrink: 0;
    }

    .legal-warning__body {
      font-size: 0.95rem;
      line-height: 1.5;
    }

    .legal-document h1 {
      font-size: 2rem;
      margin: 0 0 var(--jcs-space-2, 0.5rem);
    }

    .legal-document h2 {
      margin-top: var(--jcs-space-6, 1.5rem);
      font-size: 1.25rem;
    }

    .legal-document p,
    .legal-document li {
      line-height: 1.65;
      color: #0f1b22;
    }

    .legal-version {
      color: #5a6b75;
      font-size: 0.9rem;
      margin-bottom: var(--jcs-space-6, 1.5rem);
    }

    .legal-foot {
      display: flex;
      justify-content: space-between;
      padding-top: var(--jcs-space-6, 1.5rem);
      margin-top: var(--jcs-space-8, 2rem);
      border-top: 1px solid #e7ecf0;
    }
  `],
})
export class PrivacyPolicyPage {}
