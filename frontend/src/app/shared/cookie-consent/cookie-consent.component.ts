import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { CookieConsentService, type CookieChoice } from './cookie-consent.service';

// ============================================================================
//  CookieConsentBanner — Wave 11 slice 11.4.
//
//  Standalone bottom-bar component that renders ONLY when
//  <c>CookieConsentService.shouldShowBanner</c> is true. The component
//  is rendered at the root shell so it survives navigation between
//  public + auth + app subtrees (mounting the banner per-route would
//  re-show it on every navigation).
//
//  <para>
//  <b>Layout</b>: `position: fixed; bottom: 0; left: 0; right: 0;` with
//  a max-width content container + horizontal action buttons. The
//  banner is dismissable via the "Aceptar todas" + "Solo esenciales"
//  buttons. There is NO dismissal-without-deciding (no X button): every
//  visitor MUST pick a tier so the localStorage key is set.
//
//  <b>Copy</b>: Spanish / Jade-branded. Mirrors the welcome email tone
//  so the two consent surfaces feel coherent.
//  </para>
//
//  <para>
//  <b>Accessibility</b>:
//  <list type="bullet">
//  <item><c>role="dialog"</c> + <c>aria-label</c> for screen readers.</item>
//  <item>The banner uses high-contrast text against the dark slate
//  background — meets WCAG AA contrast (the muted subtext is intentional,
//  not the primary action).</item>
//  <item>The buttons are full-width on mobile; visually balanced on
//  desktop via grid.</item>
//  </list>
//  </para>
// ============================================================================

@Component({
  selector: 'jcs-cookie-consent-banner',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @if (service.shouldShowBanner()) {
      <aside
        class="cc-banner"
        role="dialog"
        aria-modal="false"
        aria-label="Aviso de cookies"
        data-testid="cookie-consent-banner">
        <div class="cc-inner">
          <div class="cc-copy">
            <h2 class="cc-title">Tu privacidad importa</h2>
            <p class="cc-body">
              Usamos cookies esenciales para que la plataforma funcione.
              Con tu permiso, también usamos cookies analíticas para
              entender cómo mejorarla.
              <a routerLink="/legal/privacy" target="_blank" rel="noopener">Más info</a>
            </p>
          </div>
          <div class="cc-actions">
            <button
              type="button"
              class="cc-btn cc-btn--ghost"
              data-testid="cookie-choice-essential"
              [disabled]="service.loading()"
              (click)="choose('essential')">
              Solo esenciales
            </button>
            <button
              type="button"
              class="cc-btn cc-btn--primary"
              data-testid="cookie-choice-all"
              [disabled]="service.loading()"
              (click)="choose('all')">
              Aceptar todas
            </button>
          </div>
        </div>
      </aside>
    }
  `,
  styles: [`
    .cc-banner {
      position: fixed;
      left: 0;
      right: 0;
      bottom: 0;
      z-index: 60;
      background: rgba(15, 27, 34, 0.98);
      color: #e7ecf0;
      border-top: 1px solid rgba(255, 255, 255, 0.1);
      box-shadow: 0 -10px 30px rgba(0, 0, 0, 0.25);
      backdrop-filter: blur(8px);
      -webkit-backdrop-filter: blur(8px);
    }

    .cc-inner {
      max-width: 1100px;
      margin: 0 auto;
      display: grid;
      grid-template-columns: 1fr auto;
      gap: 1rem;
      padding: 1rem 1.25rem;
      align-items: center;
    }

    @media (max-width: 720px) {
      .cc-inner {
        grid-template-columns: 1fr;
      }
      .cc-actions {
        flex-direction: row;
      }
    }

    .cc-title {
      margin: 0 0 0.25rem;
      font-size: 1rem;
      font-weight: 600;
    }

    .cc-body {
      margin: 0;
      font-size: 0.9rem;
      line-height: 1.5;
      color: #b7c2cc;
    }

    .cc-body a {
      color: #2fdb78;
      text-decoration: underline;
    }

    .cc-actions {
      display: flex;
      gap: 0.5rem;
      flex-shrink: 0;
    }

    .cc-btn {
      padding: 0.55rem 1rem;
      border-radius: 6px;
      font-weight: 600;
      font-size: 0.9rem;
      border: 1px solid transparent;
      cursor: pointer;
      transition: filter 150ms ease;
    }

    .cc-btn:disabled {
      cursor: not-allowed;
      opacity: 0.5;
    }

    .cc-btn:not(:disabled):hover {
      filter: brightness(1.1);
    }

    .cc-btn--ghost {
      background: transparent;
      color: #e7ecf0;
      border-color: rgba(255, 255, 255, 0.25);
    }

    .cc-btn--primary {
      background: #2fdb78;
      color: #0f1b22;
      border-color: #2fdb78;
    }
  `],
})
export class CookieConsentBannerComponent {
  protected readonly service = inject(CookieConsentService);

  protected async choose(choice: CookieChoice): Promise<void> {
    await this.service.setChoice(choice);
  }
}
