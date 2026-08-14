import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { DatePipe } from '@angular/common';
import { RouterLink } from '@angular/router';
import { AdminApiService, PagedSubscriptions, SubscriptionStatus } from '@core/api/admin-api.service';

const STATUSES: Array<SubscriptionStatus | ''> = [
  '', 'Pending', 'Active', 'Trial', 'PastDue', 'Suspended', 'Cancelled',
];

@Component({
  selector: 'jcs-admin-subscriptions-list',
  standalone: true,
  imports: [DatePipe, RouterLink],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <section class="jcs-card jcs-card--glow admin-list">
      <header class="list-header">
        <h2>Suscripciones</h2>
        <select [value]="status()" (change)="onStatusChange($any($event.target).value)" aria-label="Filtrar por estado">
          @for (s of statuses; track s) {
            <option [value]="s">{{ s || 'Todos' }}</option>
          }
        </select>
      </header>

      @if (loading()) {
        <p class="jcs-muted">Cargando…</p>
      } @else if (error()) {
        <p class="jcs-error">{{ error() }}</p>
      } @else if (data() && data()!.items.length === 0) {
        <p class="jcs-muted">Sin suscripciones para este filtro.</p>
      } @else if (data()) {
        <table class="jcs-table">
          <thead>
            <tr>
              <th>Usuario</th>
              <th>Plan</th>
              <th>Estado</th>
              <th>Periodo fin</th>
              <th>Actualizado</th>
              <th></th>
            </tr>
          </thead>
          <tbody>
            @for (s of data()!.items; track s.id) {
              <tr>
                <td>
                  <div class="owner">
                    <strong>{{ s.ownerDisplayName }}</strong>
                    <small class="jcs-muted">{{ s.ownerEmail }}</small>
                  </div>
                </td>
                <td><span class="jcs-pill">{{ s.planCode }}</span></td>
                <td><span class="jcs-pill jcs-pill--status" [attr.data-status]="s.status">{{ s.status }}</span></td>
                <td>{{ s.currentPeriodEnd | date:'medium' }}</td>
                <td>{{ s.updatedAt | date:'short' }}</td>
                <td><a [routerLink]="['/admin/subscriptions', s.id]" class="jcs-link">Ver</a></td>
              </tr>
            }
          </tbody>
        </table>
        <footer class="list-footer">
          <span class="jcs-muted">Total: {{ data()!.total }} · página {{ data()!.page }} de {{ totalPages() }}</span>
          <div class="pager">
            <button type="button" class="jcs-btn jcs-btn--ghost" [disabled]="page() <= 1" (click)="prev()">‹ Anterior</button>
            <button type="button" class="jcs-btn jcs-btn--ghost" [disabled]="page() >= totalPages()" (click)="next()">Siguiente ›</button>
          </div>
        </footer>
      }
    </section>
  `,
  styles: [`
    .admin-list { padding: 24px; }
    .list-header { display: flex; justify-content: space-between; align-items: center; margin-bottom: 16px; }
    .list-header h2 { margin: 0; font-size: 1.25rem; }
    .list-header select { padding: 6px 10px; border-radius: var(--radius-sm); background: var(--bg-card-soft); color: var(--text-main); border: 1px solid var(--border); }
    .jcs-table { width: 100%; border-collapse: collapse; }
    .jcs-table th { text-align: left; padding: 10px 8px; font-weight: 600; color: var(--text-secondary); border-bottom: 1px solid var(--border); font-size: 0.85rem; }
    .jcs-table td { padding: 12px 8px; border-bottom: 1px solid var(--border-soft); }
    .owner { display: flex; flex-direction: column; gap: 2px; }
    .owner small { font-size: 0.78rem; }
    .jcs-pill { display: inline-block; padding: 2px 8px; border-radius: 999px; font-size: 0.78rem; background: var(--bg-card-soft); color: var(--text-secondary); }
    .jcs-pill--status[data-status="Active"] { background: var(--green-soft); color: var(--green); }
    .jcs-pill--status[data-status="Trial"] { background: rgba(74, 168, 255, 0.12); color: var(--blue); }
    .jcs-pill--status[data-status="PastDue"] { background: rgba(245, 165, 36, 0.12); color: var(--yellow); }
    .jcs-pill--status[data-status="Suspended"], .jcs-pill--status[data-status="Cancelled"] { background: rgba(255, 64, 87, 0.12); color: var(--red); }
    .jcs-link { color: var(--green); text-decoration: none; }
    .jcs-link:hover { text-decoration: underline; }
    .list-footer { display: flex; justify-content: space-between; align-items: center; margin-top: 16px; padding-top: 16px; border-top: 1px solid var(--border-soft); }
    .pager { display: flex; gap: 8px; }
  `],
})
export class AdminSubscriptionsListPage {
  private readonly api = inject(AdminApiService);

  readonly statuses = STATUSES;
  readonly status = signal<SubscriptionStatus | ''>('');
  readonly page = signal(1);
  readonly pageSize = 20;
  readonly data = signal<PagedSubscriptions | null>(null);
  readonly loading = signal(false);
  readonly error = signal<string | null>(null);

  readonly totalPages = computed(() => {
    const d = this.data();
    if (!d) return 1;
    return Math.max(1, Math.ceil(d.total / d.pageSize));
  });

  constructor() { void this.reload(); }

  async reload(): Promise<void> {
    this.loading.set(true);
    this.error.set(null);
    try {
      this.data.set(await this.api.list(this.status(), this.page(), this.pageSize));
    } catch (e) {
      this.error.set((e as Error).message ?? 'Error desconocido');
    } finally {
      this.loading.set(false);
    }
  }

  onStatusChange(v: string): void {
    this.status.set(v as SubscriptionStatus | '');
    this.page.set(1);
    void this.reload();
  }

  prev(): void {
    if (this.page() <= 1) return;
    this.page.update((p) => p - 1);
    void this.reload();
  }

  next(): void {
    if (this.page() >= this.totalPages()) return;
    this.page.update((p) => p + 1);
    void this.reload();
  }
}
