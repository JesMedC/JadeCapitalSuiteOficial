import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { PlannerState } from './state/planner.state';
import { PlannerSessionWithComparisonDto, PlannerStatus, PLANNER_STATUS_LABELS, PLANNER_STATUS_COLORS, UpsertPlannerSessionRequest } from './api/planner.types';

/** Returns the Monday of the ISO week containing `dateStr`. */
function toMonday(dateStr: string): string {
  const d = new Date(dateStr + 'T00:00:00Z');
  const dow = d.getUTCDay() || 7; // 1..7 (Mon..Sun)
  if (dow !== 1) d.setUTCDate(d.getUTCDate() - (dow - 1));
  return d.toISOString().slice(0, 10);
}

function addDays(dateStr: string, n: number): string {
  const d = new Date(dateStr + 'T00:00:00Z');
  d.setUTCDate(d.getUTCDate() + n);
  return d.toISOString().slice(0, 10);
}

@Component({
  selector: 'jcs-planner-page',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <section class="planner-page">
      <header class="planner-header">
        <div class="planner-title-block">
          <h1 class="planner-title">Planner semanal</h1>
          <p class="planner-subtitle jcs-muted">
            @if (state.weekStart()) {
              Semana del {{ state.weekStart() }} al {{ endOfWeek() }}
            }
          </p>
        </div>
        <div class="planner-nav">
          <button type="button" class="jcs-btn jcs-btn--ghost jcs-btn--sm" (click)="prevWeek()">‹ Anterior</button>
          <button type="button" class="jcs-btn jcs-btn--ghost jcs-btn--sm" (click)="thisWeek()">Esta semana</button>
          <button type="button" class="jcs-btn jcs-btn--ghost jcs-btn--sm" (click)="nextWeek()">Siguiente ›</button>
        </div>
      </header>

      @if (state.error(); as err) {
        <div class="jcs-card jcs-card--soft planner-error" role="alert">
          <strong>Error:</strong> {{ err }}
          <button type="button" class="jcs-btn jcs-btn--ghost jcs-btn--sm" (click)="state.clearError()">Cerrar</button>
        </div>
      }

      @if (state.comparison(); as cmp) {
        <div class="comparison-panel jcs-card">
          <h3 class="cmp-title">Resumen de la semana</h3>
          <div class="cmp-grid">
            <div class="cmp-cell"><span>Planeadas</span><strong>{{ cmp.planned }}</strong></div>
            <div class="cmp-cell"><span>Completadas</span><strong class="pos">{{ cmp.completed }}</strong></div>
            <div class="cmp-cell"><span>Saltadas</span><strong class="muted">{{ cmp.skipped }}</strong></div>
            <div class="cmp-cell"><span>Canceladas</span><strong class="neg">{{ cmp.cancelled }}</strong></div>
            <div class="cmp-cell"><span>Trades reales</span><strong>{{ cmp.actualTrades }}</strong></div>
            <div class="cmp-cell"><span>P&amp;L semana</span><strong [class.pos]="cmp.totalPnl > 0" [class.neg]="cmp.totalPnl < 0">{{ formatPnl(cmp.totalPnl) }}</strong></div>
          </div>
        </div>
      }

      <div class="planner-new">
        <button type="button" class="jcs-btn jcs-btn--primary" (click)="toggleNew()">
          @if (showNew()) { Cancelar } @else { + Nueva sesión }
        </button>
      </div>

      @if (showNew()) {
        <form class="new-form jcs-card" (submit)="$event.preventDefault(); saveNew()">
          <div class="form-row">
            <label>Fecha</label>
            <input type="date" required
                   [value]="newDate()"
                   (input)="newDate.set(asInputValue($event))" />
          </div>
          <div class="form-row form-row--double">
            <div>
              <label>Hora inicio</label>
              <input type="time"
                     [value]="newStartTime()"
                     (input)="newStartTime.set(asInputValue($event))" />
            </div>
            <div>
              <label>Hora fin</label>
              <input type="time"
                     [value]="newEndTime()"
                     (input)="newEndTime.set(asInputValue($event))" />
            </div>
          </div>
          <div class="form-row">
            <label>Símbolo (opcional)</label>
            <input type="text" maxlength="20" placeholder="EURUSD"
                   [value]="newSymbol()"
                   (input)="newSymbol.set(asInputValue($event))" />
          </div>
          <div class="form-row">
            <label>Notas (opcional)</label>
            <textarea rows="3" maxlength="500"
                      [value]="newNotes()"
                      (input)="newNotes.set(asInputValue($event))"></textarea>
          </div>
          <div class="form-actions">
            <button type="submit" class="jcs-btn jcs-btn--primary" [disabled]="state.isSaving()">
              @if (state.isSaving()) { Guardando... } @else { Guardar sesión }
            </button>
          </div>
        </form>
      }

      @if (state.isLoading()) {
        <p class="jcs-muted">Cargando semana...</p>
      }

      @if (!state.isLoading() && state.sessions().length === 0 && state.hasWeek()) {
        <div class="empty-state jcs-card">
          <p class="jcs-muted">No hay sesiones planeadas para esta semana.</p>
        </div>
      }

      <ul class="session-list">
        @for (entry of state.sessions(); track entry.session.id) {
          <li class="session-item jcs-card"
              [class.follows]="entry.comparison.followsPlan"
              [class.off]="!entry.comparison.followsPlan && entry.session.status === 2">
            <div class="session-main">
              <div class="session-head">
                <span class="session-date">{{ entry.session.sessionDate }}</span>
                <span class="status-badge" [style.background]="statusColor(entry.session.status)">
                  {{ statusLabel(entry.session.status) }}
                </span>
              </div>
              <div class="session-times">
                @if (entry.session.plannedStartTime) {
                  <span>{{ entry.session.plannedStartTime }}</span>
                }
                @if (entry.session.plannedStartTime && entry.session.plannedEndTime) {
                  <span> – </span>
                }
                @if (entry.session.plannedEndTime) {
                  <span>{{ entry.session.plannedEndTime }}</span>
                }
                @if (!entry.session.plannedStartTime && !entry.session.plannedEndTime) {
                  <span class="jcs-muted">Sin horario</span>
                }
                @if (entry.session.symbol) {
                  <span class="session-symbol">{{ entry.session.symbol }}</span>
                }
              </div>
              @if (entry.session.notes) {
                <p class="session-notes">{{ entry.session.notes }}</p>
              }
              <div class="session-comparison">
                <span>Trades: {{ entry.comparison.actualTradeCount }}</span>
                <span>Cerrados: {{ entry.comparison.actualClosedTradeCount }}</span>
                @if (entry.comparison.actualSymbols.length > 0) {
                  <span>Symbols: {{ entry.comparison.actualSymbols.join(', ') }}</span>
                }
                @if (entry.session.status === 2) {
                  <span [class.pos]="entry.comparison.followsPlan" [class.neg]="!entry.comparison.followsPlan">
                    {{ entry.comparison.followsPlan ? '✓ Sigue el plan' : '✗ No siguió el plan' }}
                  </span>
                }
              </div>
            </div>
            <div class="session-actions">
              <select [value]="entry.session.status"
                      (change)="changeStatus(entry.session.id, $any($event.target).value)">
                <option [value]="1">{{ statusLabel(1) }}</option>
                <option [value]="2">{{ statusLabel(2) }}</option>
                <option [value]="3">{{ statusLabel(3) }}</option>
                <option [value]="4">{{ statusLabel(4) }}</option>
              </select>
            </div>
          </li>
        }
      </ul>
    </section>
  `,
  styles: [`
    :host { display: block; }
    .planner-page { padding: var(--sp-6); max-width: 1100px; margin: 0 auto; }
    .planner-header { display: flex; justify-content: space-between; align-items: flex-start; gap: var(--sp-4); margin-bottom: var(--sp-6); flex-wrap: wrap; }
    .planner-title { margin: 0 0 var(--sp-2); font-size: var(--fs-3xl); font-weight: 700; }
    .planner-subtitle { margin: 0; font-size: var(--fs-sm); }
    .planner-nav { display: flex; gap: var(--sp-2); flex-wrap: wrap; }
    .planner-error { display: flex; align-items: center; gap: var(--sp-3); margin-bottom: var(--sp-4); border-color: var(--red); }
    .comparison-panel { margin-bottom: var(--sp-6); }
    .cmp-title { margin: 0 0 var(--sp-3); font-size: var(--fs-base); text-transform: uppercase; letter-spacing: 0.08em; color: var(--text-muted); }
    .cmp-grid { display: grid; grid-template-columns: repeat(auto-fit, minmax(140px, 1fr)); gap: var(--sp-4); }
    .cmp-cell { display: flex; flex-direction: column; gap: var(--sp-1); }
    .cmp-cell span { font-size: 0.65rem; text-transform: uppercase; letter-spacing: 0.08em; color: var(--text-muted); }
    .cmp-cell strong { font-size: var(--fs-2xl); font-weight: 700; font-variant-numeric: tabular-nums; }
    .planner-new { margin-bottom: var(--sp-4); }
    .new-form { display: flex; flex-direction: column; gap: var(--sp-3); margin-bottom: var(--sp-4); }
    .form-row { display: flex; flex-direction: column; gap: var(--sp-2); }
    .form-row--double { display: grid; grid-template-columns: 1fr 1fr; gap: var(--sp-3); }
    .form-row label { font-size: 0.65rem; text-transform: uppercase; letter-spacing: 0.08em; color: var(--text-muted); }
    .form-row input, .form-row textarea {
      padding: var(--sp-2) var(--sp-3);
      background: var(--bg-elevated);
      border: 1px solid var(--border);
      border-radius: var(--radius-sm);
      color: var(--text-main);
      font-family: inherit;
    }
    .form-row input:focus, .form-row textarea:focus { outline: 2px solid var(--border-active); outline-offset: 2px; }
    .form-actions { display: flex; justify-content: flex-end; }
    .empty-state { text-align: center; padding: var(--sp-8); }
    .session-list { list-style: none; padding: 0; margin: 0; display: flex; flex-direction: column; gap: var(--sp-3); }
    .session-item { display: flex; justify-content: space-between; align-items: flex-start; gap: var(--sp-4); padding: var(--sp-4); }
    .session-item.follows { border-left: 3px solid var(--green); }
    .session-item.off { border-left: 3px solid var(--yellow); }
    .session-main { display: flex; flex-direction: column; gap: var(--sp-2); flex: 1; min-width: 0; }
    .session-head { display: flex; align-items: center; gap: var(--sp-3); flex-wrap: wrap; }
    .session-date { font-weight: 700; font-variant-numeric: tabular-nums; }
    .status-badge { padding: 2px var(--sp-2); border-radius: var(--radius-xs); color: var(--bg-main); font-size: 0.7rem; font-weight: 700; text-transform: uppercase; letter-spacing: 0.04em; }
    .session-times { display: flex; gap: var(--sp-2); align-items: center; flex-wrap: wrap; color: var(--text-secondary); font-variant-numeric: tabular-nums; }
    .session-symbol { padding: 2px var(--sp-2); background: var(--bg-elevated); border-radius: var(--radius-xs); font-weight: 600; }
    .session-notes { margin: 0; color: var(--text-muted); font-size: var(--fs-sm); }
    .session-comparison { display: flex; gap: var(--sp-4); font-size: var(--fs-xs); color: var(--text-muted); flex-wrap: wrap; }
    .session-actions select {
      padding: var(--sp-2) var(--sp-3);
      background: var(--bg-elevated);
      border: 1px solid var(--border);
      border-radius: var(--radius-sm);
      color: var(--text-main);
    }
    .pos { color: var(--green); }
    .neg { color: var(--red); }
    .muted { color: var(--text-muted); }
    @media (max-width: 767px) {
      .planner-page { padding: var(--sp-4); }
      .form-row--double { grid-template-columns: 1fr; }
    }
  `],
})
export class PlannerPage {
  readonly state = inject(PlannerState);

  readonly showNew = signal(false);
  readonly newDate = signal('');
  readonly newStartTime = signal<string | null>(null);
  readonly newEndTime = signal<string | null>(null);
  readonly newSymbol = signal('');
  readonly newNotes = signal('');

  constructor() {
    const today = new Date().toISOString().slice(0, 10);
    this.state.loadWeek(toMonday(today));
    this.newDate.set(today);
  }

  endOfWeek(): string {
    const ws = this.state.weekStart();
    if (!ws) return '';
    return addDays(ws, 6);
  }

  prevWeek(): void {
    const ws = this.state.weekStart() || toMonday(new Date().toISOString().slice(0, 10));
    this.state.loadWeek(addDays(ws, -7));
  }

  nextWeek(): void {
    const ws = this.state.weekStart() || toMonday(new Date().toISOString().slice(0, 10));
    this.state.loadWeek(addDays(ws, 7));
  }

  thisWeek(): void {
    this.state.loadWeek(toMonday(new Date().toISOString().slice(0, 10)));
  }

  toggleNew(): void {
    this.showNew.update(v => !v);
  }

  saveNew(): void {
    if (!this.newDate()) return;
    const body: UpsertPlannerSessionRequest = {
      sessionDate: this.newDate(),
      plannedStartTime: this.newStartTime(),
      plannedEndTime: this.newEndTime(),
      symbol: this.newSymbol().trim() || null,
      notes: this.newNotes().trim() || null,
    };
    this.state.createSessionSignal(body).then(saved => {
      if (saved) {
        this.showNew.set(false);
        this.newDate.set('');
        this.newStartTime.set(null);
        this.newEndTime.set(null);
        this.newSymbol.set('');
        this.newNotes.set('');
      }
    });
  }

  changeStatus(id: string, status: number): void {
    const s = Number(status) as PlannerStatus;
    if (![1, 2, 3, 4].includes(s)) return;
    this.state.changeStatus(id, s);
  }

  statusLabel(s: PlannerStatus): string {
    return PLANNER_STATUS_LABELS[s];
  }

  statusColor(s: PlannerStatus): string {
    return PLANNER_STATUS_COLORS[s];
  }

  formatPnl(n: number): string {
    return (n >= 0 ? '+' : '') + n.toFixed(2);
  }

  asInputValue(event: Event): string {
    return (event.target as HTMLInputElement | HTMLTextAreaElement).value;
  }
}
