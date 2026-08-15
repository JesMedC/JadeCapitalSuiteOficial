import { ChangeDetectionStrategy, Component, computed, inject, input, signal } from '@angular/core';
import { DatePipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { AdminApiService, SubscriptionDetail, SubscriptionHistoryItem } from '@core/api/admin-api.service';

@Component({
  selector: 'jcs-admin-subscription-detail',
  standalone: true,
  imports: [DatePipe, FormsModule, RouterLink],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <a routerLink="/admin/subscriptions" class="back">← Volver al listado</a>

    @if (loading()) {
      <p class="jcs-muted">Cargando…</p>
    } @else if (error()) {
      <p class="jcs-error">{{ error() }}</p>
    } @else if (sub()) {
      <section class="jcs-card jcs-card--glow detail">
        <header class="detail-head">
          <div>
            <h2>{{ sub()!.owner.displayName }}</h2>
            <small class="jcs-muted">{{ sub()!.owner.email }}</small>
          </div>
          <span class="jcs-pill jcs-pill--status" [attr.data-status]="sub()!.status">{{ sub()!.status }}</span>
        </header>

        <dl class="meta">
          <div><dt>Plan</dt><dd>{{ sub()!.planCode }} — {{ sub()!.planName }}</dd></div>
          <div><dt>User ID</dt><dd>{{ sub()!.userId }}</dd></div>
          @if (sub()!.trialEndsAt) {
            <div><dt>Trial fin</dt><dd>{{ sub()!.trialEndsAt | date:'medium' }}</dd></div>
          }
          <div><dt>Versión</dt><dd>{{ sub()!.version }}</dd></div>
          <div><dt>Creado</dt><dd>{{ sub()!.createdAt | date:'medium' }}</dd></div>
          @if (sub()!.updatedAt) {
            <div><dt>Actualizado</dt><dd>{{ sub()!.updatedAt | date:'medium' }}</dd></div>
          }
        </dl>

        <section class="actions">
          <h3>Acciones</h3>
          <div class="action-row">
            <label>Nuevo plan
              <select [(ngModel)]="newPlan">
                <option value="starter">starter</option>
                <option value="pro">pro</option>
                <option value="elite">elite</option>
              </select>
            </label>
            <button type="button" class="jcs-btn jcs-btn--primary" [disabled]="busy()" (click)="changeTier()">Cambiar tier</button>
          </div>
          <div class="action-row">
            <label>Motivo
              <input type="text" [(ngModel)]="cancelReason" placeholder="Ej: solicitado por el usuario" />
            </label>
            <button type="button" class="jcs-btn jcs-btn--danger" [disabled]="busy() || !cancelReason" (click)="cancel()">Cancelar</button>
          </div>
          <div class="action-row">
            <label>Nuevo fin de trial
              <input type="date" [(ngModel)]="newTrialDate" />
            </label>
            <button type="button" class="jcs-btn jcs-btn--ghost" [disabled]="busy() || !newTrialDate" (click)="extendTrial()">Extender trial</button>
          </div>
          @if (actionError()) { <p class="jcs-error">{{ actionError() }}</p> }
        </section>

        <section class="history">
          <h3>Historial</h3>
          @if (history().length === 0) {
            <p class="jcs-muted">Sin eventos.</p>
          } @else {
            <ol class="timeline">
              @for (h of history(); track h.id) {
                <li>
                  <div class="ts">{{ h.occurredAt | date:'short' }}</div>
                  <div class="body">
                    <strong>{{ h.action }}</strong> · <span class="jcs-muted">{{ h.actor }}</span>
                    <span>{{ h.priorStatus }} → {{ h.resultingStatus }}</span>
                    <span>{{ h.priorPlanCode }} → {{ h.resultingPlanCode }}</span>
                    @if (h.reason) { <span class="reason">"{{ h.reason }}"</span> }
                  </div>
                </li>
              }
            </ol>
          }
        </section>
      </section>
    }
  `,
  styles: [`
    .back { display: inline-block; margin-bottom: 16px; color: var(--green); text-decoration: none; }
    .back:hover { text-decoration: underline; }
    .detail { padding: 24px; display: flex; flex-direction: column; gap: 24px; }
    .detail-head { display: flex; justify-content: space-between; align-items: flex-start; }
    .detail-head h2 { margin: 0 0 4px; }
    .meta { display: grid; grid-template-columns: repeat(auto-fit, minmax(220px, 1fr)); gap: 12px 24px; margin: 0; }
    .meta dt { font-size: 0.78rem; color: var(--text-muted); text-transform: uppercase; letter-spacing: 0.04em; }
    .meta dd { margin: 2px 0 0; color: var(--text-main); }
    .actions h3, .history h3 { margin: 0 0 12px; font-size: 1rem; color: var(--text-secondary); }
    .action-row { display: flex; gap: 12px; align-items: flex-end; margin-bottom: 12px; flex-wrap: wrap; }
    .action-row label { display: flex; flex-direction: column; gap: 4px; font-size: 0.78rem; color: var(--text-muted); flex: 1; min-width: 200px; }
    .action-row input, .action-row select { padding: 8px 10px; border-radius: var(--radius-sm); background: var(--bg-card-soft); color: var(--text-main); border: 1px solid var(--border); }
    .jcs-pill--status[data-status="Active"] { background: var(--green-soft); color: var(--green); }
    .jcs-pill--status[data-status="Trial"] { background: rgba(74, 168, 255, 0.12); color: var(--blue); }
    .jcs-pill--status[data-status="PastDue"] { background: rgba(245, 165, 36, 0.12); color: var(--yellow); }
    .jcs-pill--status[data-status="Suspended"], .jcs-pill--status[data-status="Cancelled"] { background: rgba(255, 64, 87, 0.12); color: var(--red); }
    .timeline { list-style: none; padding: 0; margin: 0; display: flex; flex-direction: column; gap: 12px; }
    .timeline li { display: flex; gap: 16px; padding: 12px; background: var(--bg-card-soft); border-radius: var(--radius-sm); border-left: 3px solid var(--green); }
    .timeline .ts { color: var(--text-muted); font-size: 0.85rem; min-width: 110px; }
    .timeline .body { display: flex; flex-direction: column; gap: 4px; }
    .timeline .reason { color: var(--text-secondary); font-style: italic; }
    .jcs-btn--danger { background: var(--red); color: white; border: none; }
    .jcs-btn--danger:hover:not(:disabled) { background: #ff5570; }
    .jcs-btn--danger:disabled { opacity: 0.5; cursor: not-allowed; }
  `],
})
export class AdminSubscriptionDetailPage {
  private readonly api = inject(AdminApiService);

  readonly id = input.required<string>();

  readonly sub = signal<SubscriptionDetail | null>(null);
  readonly loading = signal(false);
  readonly error = signal<string | null>(null);
  readonly busy = signal(false);
  readonly actionError = signal<string | null>(null);

  newPlan = 'pro';
  cancelReason = '';
  newTrialDate = '';

  readonly history = computed<SubscriptionHistoryItem[]>(() => this.sub()?.history ?? []);

  constructor() {
    queueMicrotask(() => void this.reload());
  }

  async reload(): Promise<void> {
    const id = this.id();
    if (!id) return;
    this.loading.set(true);
    this.error.set(null);
    try {
      this.sub.set(await this.api.detail(id));
    } catch (e) {
      this.error.set((e as Error).message ?? 'Error desconocido');
    } finally {
      this.loading.set(false);
    }
  }

  async changeTier(): Promise<void> {
    const s = this.sub();
    if (!s) return;
    this.busy.set(true);
    this.actionError.set(null);
    try {
      await this.api.changeTier(s.subscriptionId, this.newPlan, s.version);
      await this.reload();
    } catch (e) {
      this.actionError.set(`Cambiar tier: ${(e as Error).message}`);
    } finally { this.busy.set(false); }
  }

  async cancel(): Promise<void> {
    const s = this.sub();
    if (!s) return;
    this.busy.set(true);
    this.actionError.set(null);
    try {
      await this.api.cancel(s.subscriptionId, this.cancelReason, s.version);
      await this.reload();
    } catch (e) {
      this.actionError.set(`Cancelar: ${(e as Error).message}`);
    } finally { this.busy.set(false); }
  }

  async extendTrial(): Promise<void> {
    const s = this.sub();
    if (!s || !this.newTrialDate) return;
    const iso = new Date(this.newTrialDate + 'T00:00:00Z').toISOString();
    this.busy.set(true);
    this.actionError.set(null);
    try {
      await this.api.extendTrial(s.subscriptionId, iso, s.version);
      await this.reload();
    } catch (e) {
      this.actionError.set(`Extender trial: ${(e as Error).message}`);
    } finally { this.busy.set(false); }
  }
}
