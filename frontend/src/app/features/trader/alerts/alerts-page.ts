import {
  ChangeDetectionStrategy,
  Component,
  computed,
  inject,
} from '@angular/core';
import { Router } from '@angular/router';
import { AlertsState } from './state/alerts.state';
import {
  AlertDto,
  RULE_LABELS,
  SEVERITY_COLORS,
} from './api/alerts.types';

// ============================================================================
//  AlertsPage — slice 3b frontend (3b.2).
//
//  Mobile-first standalone page (Signals + OnPush + SCSS). Layout:
//   - Header: title "Alertas" + active count.
//   - Toggle "Mostrar todas" / "Solo activas".
//   - List of cards. Each card shows:
//     - Severity color stripe (left border or top dot).
//     - RuleId label (or title).
//     - Body.
//     - CTA button (navigates via Router.navigateByUrl).
//     - "Marcar como leído" button → PATCH /ack.
//   - Empty state "Sin alertas activas".
//
//  State signals live in AlertsState (injected).
// ============================================================================

@Component({
  selector: 'jcs-alerts-page',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="ap-page">
      <header class="ap-head">
        <p class="jcs-muted ap-eyebrow">Avisos automáticos</p>
        <h1 class="ap-title">Alertas</h1>
        <p class="jcs-muted ap-sub">
          Detectadas por el background service cada 5&nbsp;min. Acumulan una por día por regla.
        </p>
      </header>

      <!-- ============== Error banner ============== -->
      @if (state.error(); as err) {
        <div class="ap-error" role="alert" data-testid="alerts-error">
          <span>{{ err }}</span>
          <button type="button" class="ap-error-dismiss" (click)="dismissError()">×</button>
        </div>
      }

      <!-- ============== Loading ============== -->
      @if (state.isLoading()) {
        <div class="ap-loading" aria-live="polite">
          <span class="ap-spinner" aria-hidden="true"></span>
          <span class="jcs-muted">Cargando alertas…</span>
        </div>
      }

      <!-- ============== Active count + toggle ============== -->
      @if (!state.isLoading() && !state.error()) {
        <div class="ap-toolbar">
          <span class="ap-count" data-testid="alerts-active-count">
            {{ state.activeCount() }} activa(s)
          </span>
          <label class="ap-toggle">
            <input
              type="checkbox"
              [checked]="state.showAll()"
              (change)="state.toggleShowAll()"
              data-testid="alerts-toggle-show-all" />
            <span>Mostrar todas</span>
          </label>
        </div>
      }

      <!-- ============== Empty state ============== -->
      @if (!state.isLoading() && state.visible().length === 0 && !state.error()) {
        <div class="ap-empty" data-testid="alerts-empty">
          <p>Sin alertas activas.</p>
          <p class="jcs-muted">
            El background service evalúa cada 5 minutos. Las alertas que dispare
            una regla aparecerán acá.
          </p>
        </div>
      }

      <!-- ============== Cards list ============== -->
      <ul class="ap-list" data-testid="alerts-list">
        @for (alert of state.visible(); track alert.id) {
          <li
            class="ap-card"
            [class.ap-card--acked]="!!alert.acknowledgedAt"
            [attr.data-severity]="alert.severity">
            <div class="ap-card-stripe" [style.background]="severityColor(alert)"></div>

            <div class="ap-card-body">
              <div class="ap-card-head">
                <span
                  class="ap-severity"
                  [style.color]="severityColor(alert)"
                  data-testid="alerts-severity">
                  {{ severityBadge(alert) }}
                </span>
                <span class="ap-rule">{{ ruleLabel(alert) }}</span>
                @if (alert.acknowledgedAt) {
                  <span class="ap-acked" data-testid="alerts-acked-badge">Leída</span>
                }
              </div>

              <h2 class="ap-card-title">{{ alert.title }}</h2>
              <p class="ap-card-text">{{ alert.body }}</p>

              <div class="ap-card-actions">
                @if (alert.cta; as cta) {
                  <button
                    type="button"
                    class="ap-btn ap-btn--ghost"
                    (click)="goToCta(cta.route)"
                    data-testid="alerts-cta">
                    {{ cta.label }}
                  </button>
                }
                @if (!alert.acknowledgedAt) {
                  <button
                    type="button"
                    class="ap-btn ap-btn--primary"
                    (click)="onAck(alert)"
                    data-testid="alerts-ack">
                    Marcar como leído
                  </button>
                }
              </div>
            </div>
          </li>
        }
      </ul>
    </div>
  `,
  styles: [`
    :host { display: block; }

    .ap-page {
      display: flex;
      flex-direction: column;
      gap: var(--sp-4, 16px);
      max-width: 960px;
    }

    .ap-head { margin-bottom: var(--sp-2, 8px); }
    .ap-eyebrow { font-size: 0.7rem; text-transform: uppercase; letter-spacing: 0.08em; margin: 0; }
    .ap-title { font-size: 1.8rem; font-weight: 700; margin: var(--sp-1, 4px) 0; letter-spacing: -0.02em; }
    .ap-sub { margin: 0; font-size: 0.9rem; }

    .ap-error {
      display: flex;
      align-items: center;
      justify-content: space-between;
      padding: var(--sp-3, 12px) var(--sp-4, 16px);
      background: rgba(255, 64, 87, 0.12);
      border: 1px solid var(--red, #ff4057);
      border-radius: var(--radius-md, 8px);
      color: var(--red, #ff4057);
      font-size: var(--fs-sm, 0.875rem);
    }
    .ap-error-dismiss {
      background: transparent;
      border: 0;
      color: inherit;
      cursor: pointer;
      font-size: 1.2rem;
      line-height: 1;
    }

    .ap-loading {
      display: flex;
      align-items: center;
      gap: var(--sp-3, 12px);
      padding: var(--sp-4, 16px);
    }
    .ap-spinner {
      display: inline-block;
      width: 16px;
      height: 16px;
      border: 2px solid var(--border, #e5e5e5);
      border-top-color: var(--green, #2fdb78);
      border-radius: 50%;
      animation: ap-rotate 0.7s linear infinite;
    }
    @keyframes ap-rotate { to { transform: rotate(360deg); } }

    .ap-toolbar {
      display: flex;
      align-items: center;
      justify-content: space-between;
      gap: var(--sp-3, 12px);
    }
    .ap-count {
      font-size: 0.8rem;
      font-weight: 600;
      color: var(--text-muted, #777);
    }
    .ap-toggle {
      display: inline-flex;
      align-items: center;
      gap: var(--sp-2, 8px);
      font-size: var(--fs-sm, 0.875rem);
      color: var(--text-muted, #777);
      cursor: pointer;
    }
    .ap-toggle input { accent-color: var(--green, #2fdb78); }

    .ap-empty {
      padding: var(--sp-5, 24px);
      background: var(--bg-card-soft, #f7f7f7);
      border: 1px dashed var(--border, #e5e5e5);
      border-radius: var(--radius-md, 8px);
      text-align: center;
    }
    .ap-empty p { margin: 0; }
    .ap-empty p + p { margin-top: var(--sp-2, 8px); }

    .ap-list {
      display: flex;
      flex-direction: column;
      gap: var(--sp-2, 8px);
      padding: 0;
      margin: 0;
      list-style: none;
    }
    .ap-card {
      display: flex;
      background: var(--bg-card, #fff);
      border: 1px solid var(--border, #e5e5e5);
      border-radius: var(--radius-md, 8px);
      overflow: hidden;
      transition: border-color 150ms;
    }
    .ap-card:hover { border-color: var(--border-active, #c0c0c0); }
    .ap-card--acked { opacity: 0.6; }

    .ap-card-stripe {
      width: 4px;
      flex-shrink: 0;
    }

    .ap-card-body {
      flex: 1;
      display: flex;
      flex-direction: column;
      gap: var(--sp-2, 8px);
      padding: var(--sp-3, 12px) var(--sp-4, 16px);
    }

    .ap-card-head {
      display: flex;
      align-items: center;
      gap: var(--sp-2, 8px);
      flex-wrap: wrap;
    }
    .ap-severity {
      font-size: 0.7rem;
      font-weight: 700;
      text-transform: uppercase;
      letter-spacing: 0.06em;
    }
    .ap-rule {
      font-size: 0.7rem;
      color: var(--text-muted, #777);
      background: var(--bg-card-soft, #f7f7f7);
      padding: 2px var(--sp-2, 8px);
      border-radius: 999px;
    }
    .ap-acked {
      font-size: 0.65rem;
      font-weight: 700;
      color: var(--text-muted, #777);
      background: var(--bg-card-soft, #f7f7f7);
      padding: 2px var(--sp-2, 8px);
      border-radius: 999px;
    }

    .ap-card-title {
      font-size: var(--fs-base, 1rem);
      font-weight: 600;
      margin: 0;
    }
    .ap-card-text {
      margin: 0;
      font-size: 0.9rem;
      color: var(--text-muted, #777);
      line-height: 1.45;
    }

    .ap-card-actions {
      display: flex;
      gap: var(--sp-2, 8px);
      flex-wrap: wrap;
      margin-top: var(--sp-1, 4px);
    }

    .ap-btn {
      display: inline-flex;
      align-items: center;
      gap: var(--sp-2, 8px);
      padding: var(--sp-2, 8px) var(--sp-4, 16px);
      border: 1px solid transparent;
      border-radius: var(--radius-sm, 6px);
      font-size: var(--fs-sm, 0.875rem);
      font-weight: 500;
      cursor: pointer;
      transition: background 150ms;
    }
    .ap-btn--primary {
      background: var(--green, #2fdb78);
      color: #050B10;
    }
    .ap-btn--primary:hover { background: var(--green-hover, #28c068); }
    .ap-btn--primary:disabled { opacity: 0.5; cursor: not-allowed; }
    .ap-btn--ghost {
      background: transparent;
      color: var(--text-main, #1a1a1a);
      border-color: var(--border, #e5e5e5);
    }
    .ap-btn--ghost:hover { background: var(--bg-hover, #f0f0f0); }
  `],
})
export class AlertsPage {
  readonly state = inject(AlertsState);
  private readonly router = inject(Router);

  protected readonly SEVERITY_COLORS = SEVERITY_COLORS;
  protected readonly RULE_LABELS = RULE_LABELS;

  constructor() {
    void this.state.loadAll();
  }

  severityColor(alert: AlertDto): string {
    return SEVERITY_COLORS[alert.severity];
  }

  severityBadge(alert: AlertDto): string {
    return alert.severity;
  }

  ruleLabel(alert: AlertDto): string {
    return RULE_LABELS[alert.ruleId] ?? alert.ruleId;
  }

  goToCta(route: string): void {
    void this.router.navigateByUrl(route);
  }

  async onAck(alert: AlertDto): Promise<void> {
    await this.state.acknowledge(alert.id);
  }

  dismissError(): void {
    // Clear the error without reloading — preserves current list.
    // The state's formatError doesn't expose a setter, so we reset and reload.
    this.state.reset();
    void this.state.loadAll();
  }
}