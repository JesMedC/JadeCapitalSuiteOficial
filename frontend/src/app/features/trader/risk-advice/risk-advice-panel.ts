import { ChangeDetectionStrategy, Component, inject, input } from '@angular/core';
import { RiskAdviceState } from './state/risk-advice.state';
import { RiskAdviceRequest } from './api/risk-advice.types';

// ============================================================================
//  RiskAdvicePanel — slice 5c.1 (Wave 5, AI Risk Advisor Pre-Trade).
//
//  Standalone Signals + OnPush component that renders the AI risk advisory
//  for a given trade. The parent (pre-trade-checklist-page or trade-detail)
//  passes the current trade parameters via the `request` input. The panel
//  fires the advisory on demand and renders the parsed result.
//
//  Render states:
//   - Idle (no advisory yet) — empty card with a CTA "Run AI check".
//   - Loading — spinner.
//   - Advisory (allow/warning/block) — colored card with action badge + reason.
//   - Error — error banner with retry CTA.
// ============================================================================

@Component({
  selector: 'jcs-risk-advice-panel',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <section class="risk-advice-panel jcs-card">
      <header class="risk-advice-header">
        <h3 class="risk-advice-title">AI Risk Advisor</h3>
        <span class="risk-advice-hint jcs-muted">Pre-trade advisory</span>
      </header>

      @if (state.isLoading()) {
        <div class="risk-advice-loading" role="status">
          <span class="spinner"></span>
          <span>Analizando tu historial...</span>
        </div>
      }

      @if (!state.isLoading() && state.error(); as err) {
        <div class="risk-advice-error" role="alert">
          <strong>No pudimos obtener la advisory.</strong>
          <span>{{ err }}</span>
          <button type="button" class="jcs-btn jcs-btn--ghost jcs-btn--sm" (click)="fire()">
            Reintentar
          </button>
        </div>
      }

      @if (!state.isLoading() && !state.error() && state.currentAdvice(); as advice) {
        <div class="risk-advice-card" [attr.data-action]="advice.action">
          <div class="risk-advice-badge">
            <span class="badge badge--{{ advice.action }}">{{ actionLabel(advice.action) }}</span>
          </div>
          <p class="risk-advice-reason">{{ advice.reason }}</p>
          <footer class="risk-advice-meta">
            <span class="jcs-muted">{{ advice.model }}</span>
            <span class="jcs-muted">{{ advice.latencyMs }}ms</span>
          </footer>
          @if (advice.action === 'block') {
            <p class="risk-advice-warning">
              AI recomienda no abrir este trade. ¿Querés continuar manualmente?
            </p>
          }
        </div>
      }

      @if (!state.isLoading() && !state.error() && !state.currentAdvice()) {
        <div class="risk-advice-empty">
          <p class="jcs-muted">Aún no corrimos el advisor para este trade.</p>
          <button type="button" class="jcs-btn jcs-btn--ghost" (click)="fire()" [disabled]="!request()">
            Run AI check
          </button>
        </div>
      }
    </section>
  `,
  styles: [`
    .risk-advice-panel { padding: var(--sp-4); display: flex; flex-direction: column; gap: var(--sp-3); }
    .risk-advice-header { display: flex; justify-content: space-between; align-items: baseline; }
    .risk-advice-title { margin: 0; font-size: var(--fs-md); font-weight: 600; }
    .risk-advice-hint { font-size: 0.75rem; }
    .risk-advice-loading { display: flex; align-items: center; gap: var(--sp-2); padding: var(--sp-3); }
    .spinner {
      width: 16px; height: 16px;
      border: 2px solid var(--border);
      border-top-color: var(--green);
      border-radius: 50%;
      animation: spin 0.8s linear infinite;
    }
    @keyframes spin { to { transform: rotate(360deg); } }
    .risk-advice-error { display: flex; flex-direction: column; gap: var(--sp-2); padding: var(--sp-3); border: 1px solid var(--red); border-radius: var(--radius-sm); }
    .risk-advice-card { display: flex; flex-direction: column; gap: var(--sp-2); padding: var(--sp-3); border-radius: var(--radius-sm); }
    .risk-advice-card[data-action="allow"] { background: rgba(47, 219, 120, 0.08); border: 1px solid var(--green); }
    .risk-advice-card[data-action="warning"] { background: rgba(255, 184, 0, 0.08); border: 1px solid #ffb800; }
    .risk-advice-card[data-action="block"] { background: rgba(255, 64, 87, 0.08); border: 1px solid var(--red); }
    .badge { display: inline-block; padding: 2px 8px; border-radius: 4px; font-size: 0.7rem; font-weight: 600; text-transform: uppercase; letter-spacing: 0.05em; }
    .badge--allow { background: var(--green); color: #050B10; }
    .badge--warning { background: #ffb800; color: #050B10; }
    .badge--block { background: var(--red); color: #fff; }
    .risk-advice-reason { margin: 0; font-size: var(--fs-sm); line-height: 1.4; }
    .risk-advice-meta { display: flex; justify-content: space-between; font-size: 0.7rem; }
    .risk-advice-warning { margin: 0; font-size: 0.75rem; font-weight: 500; color: var(--red); }
    .risk-advice-empty { display: flex; flex-direction: column; gap: var(--sp-2); align-items: flex-start; }
  `],
})
export class RiskAdvicePanel {
  readonly state = inject(RiskAdviceState);

  /** The trade parameters used to drive the AI advisory. */
  readonly request = input<RiskAdviceRequest | null>(null);

  /** Trigger the AI advisory against the current request. */
  async fire(): Promise<void> {
    const req = this.request();
    if (!req) return;
    await this.state.requestAdvice(req);
  }

  /** Translate the action into a Spanish label. */
  actionLabel(action: string): string {
    switch (action) {
      case 'allow': return 'OK';
      case 'warning': return 'Cuidado';
      case 'block': return 'Bloquear';
      default: return action;
    }
  }
}
