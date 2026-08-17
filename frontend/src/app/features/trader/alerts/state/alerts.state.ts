import { Injectable, computed, inject, signal } from '@angular/core';
import { HttpErrorResponse } from '@angular/common/http';
import { AlertDto, AlertProblem } from '../api/alerts.types';
import { AlertsService } from '../api/alerts.service';

// ============================================================================
//  AlertsState — slice 3b frontend.
//
//  Signal store for the user's alerts. Two views from the same backing list:
//   - active()  → list filtered to un-acked AND un-expired.
//   - all()     → the raw list (audit trail).
//   - showAll() → UI toggle; when true the page renders all() instead of active().
//
//  Acknowledge mutates the signal in-place so the UI updates without a refetch.
// ============================================================================

@Injectable({ providedIn: 'root' })
export class AlertsState {
  private readonly api = inject(AlertsService);

  readonly list = signal<AlertDto[]>([]);
  readonly showAll = signal<boolean>(false);
  readonly isLoading = signal<boolean>(false);
  readonly error = signal<string | null>(null);

  readonly visible = computed<AlertDto[]>(() => {
    const raw = this.list();
    return this.showAll() ? raw : raw.filter(a => this.isActive(a));
  });

  readonly activeCount = computed<number>(
    () => this.list().filter(a => this.isActive(a)).length,
  );

  async loadAll(): Promise<void> {
    this.isLoading.set(true);
    this.error.set(null);
    try {
      const items = await this.api.list(false);
      this.list.set(items);
    } catch (e) {
      this.error.set(this.formatError(e));
    } finally {
      this.isLoading.set(false);
    }
  }

  async acknowledge(id: string): Promise<boolean> {
    this.error.set(null);
    try {
      const updated = await this.api.acknowledge(id);
      this.list.update(items =>
        items.map(a => (a.id === id ? updated : a)),
      );
      return true;
    } catch (e) {
      this.error.set(this.formatError(e));
      return false;
    }
  }

  toggleShowAll(): void {
    this.showAll.update(v => !v);
  }

  reset(): void {
    this.list.set([]);
    this.showAll.set(false);
    this.isLoading.set(false);
    this.error.set(null);
  }

  private isActive(a: AlertDto): boolean {
    if (a.acknowledgedAt) return false;
    if (a.expiresAt && new Date(a.expiresAt).getTime() < Date.now()) return false;
    return true;
  }

  formatError(err: unknown): string {
    if (err instanceof HttpErrorResponse) {
      const body = (err.error ?? {}) as AlertProblem;
      const detail = body.detail ?? body.title ?? err.message;
      if (detail) return detail;
      return `Error HTTP ${err.status}.`;
    }
    if (err instanceof Error && err.message) return err.message;
    return 'Ocurrió un error inesperado.';
  }
}