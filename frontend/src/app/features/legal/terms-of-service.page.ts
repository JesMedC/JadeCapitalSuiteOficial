import { ChangeDetectionStrategy, Component } from '@angular/core';
import { RouterLink } from '@angular/router';

// ============================================================================
//  TermsOfServicePage — Wave 11 slice 11.4.
//
//  Lazy-loaded from /legal/terms. Renders the placeholder ToS copy until
//  legal counsel approves the canonical version. The "DO NOT DEPLOY"
//  warning banner is prominently displayed at the top so any operator
//  staging this in a public environment gets the memo immediately.
//
//  <para>
//  Why placeholder copy + warning banner (instead of blocking the route):
//  * The route MUST resolve so the registration form's link does NOT 404.
//  * The banner MUST stay until the canonical copy lands.
//  * The Markdown source lives in <c>frontend/src/assets/legal/terms-of-service.md</c>;
//    this page renders an inline excerpt + a "see full document on
//    storage" pointer so the FE stays free of large string literals.
//  </para>
// ============================================================================

@Component({
  selector: 'jcs-terms-of-service',
  standalone: true,
  imports: [RouterLink],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <section class="legal-page" data-testid="terms-of-service-page">
      <header class="legal-warning" data-testid="legal-warning-banner" role="alert">
        <span class="legal-warning__icon" aria-hidden="true">⚠️</span>
        <div class="legal-warning__body">
          <strong>DO NOT DEPLOY WITHOUT LEGAL COUNSEL SIGN-OFF.</strong>
          The text below is PLACEHOLDER copy generated for development
          sanity. Replace with the canonical ToS reviewed by legal
          counsel + product before the v1.0 GA release.
        </div>
      </header>

      <article class="legal-document">
        <h1>Términos de Servicio — Jade Capital Suite</h1>
        <p class="legal-version">Versión: placeholder (pre-v1.0) · Pendiente de revisión legal</p>

        <h2>1. Aceptación</h2>
        <p>
          Al crear una cuenta en Jade Capital Suite (el "Servicio"),
          aceptás haber leído, comprendido y estar obligado por estos
          Términos de Servicio. Si no estás de acuerdo, no crees la
          cuenta.
        </p>

        <h2>2. Elegibilidad</h2>
        <p>
          Debés ser mayor de 18 años (o la mayoría de edad en tu
          jurisdicción). Al aceptar estos términos declarás cumplir
          con este requisito.
        </p>

        <h2>3. Responsabilidades de la cuenta</h2>
        <ul>
          <li>Mantené la confidencialidad de tus credenciales.</li>
          <li>Proporcioná información veraz y actualizada.</li>
          <li>Sos responsable de toda actividad bajo tu cuenta.</li>
        </ul>

        <h2>4. Uso permitido</h2>
        <p>
          El Servicio es una herramienta de registro y análisis de
          actividad de trading personal. NO emitimos señales ni
          recomendaciones de inversión.
        </p>

        <h2>5. Baja de la cuenta</h2>
        <p>
          Podés solicitar la eliminación de tu cuenta en cualquier
          momento mediante <code>DELETE /api/users/me/account</code>.
          Hay un período de gracia de 30 días antes de la anonimización
          irreversible.
        </p>

        <h2>6. Contacto</h2>
        <p>
          Consultas legales: <a href="mailto:legal&#64;jadecapital.com">legal&#64;jadecapital.com</a>.
        </p>
      </article>

      <footer class="legal-foot">
        <a routerLink="/legal/privacy">Política de privacidad →</a>
        <a routerLink="/">← Volver al inicio</a>
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
export class TermsOfServicePage {}
