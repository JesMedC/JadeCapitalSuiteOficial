import { Injectable, computed, inject, signal } from '@angular/core';
import { HttpErrorResponse } from '@angular/common/http';
import {
  StrategyAnalyticsDto,
  StrategyDto,
  StrategyProblem,
  UpsertStrategyRequest,
} from '../api/strategies.types';
import { StrategiesService } from '../api/strategies.service';

// ============================================================================
//  StrategiesState — slice 3a frontend.
//
//  Signal store for the active user's strategies. The page renders four
//  states from this signal set:
//   - isLoading()                    → spinner
//   - error()                        → red banner (formatError)
//   - list().length === 0 && !error() → empty-state copy
//   - list().length > 0              → cards
//
//  Each strategy can have analytics loaded on demand (signal<StrategyAnalyticsDto|null>)
//  keyed by strategy id via a map. The page fetches analytics when a card
//  is expanded; we keep them cached in memory until the user navigates
//  away (refresh-on-load invalidates).
// ============================================================================

@Injectable({ providedIn: 'root' })
export class StrategiesState {
  private readonly api = inject(StrategiesService);

  readonly list = signal<StrategyDto[]>([]);
  readonly selectedId = signal<string | null>(null);
  readonly analyticsById = signal<Record<string, StrategyAnalyticsDto>>({});
  readonly isLoading = signal(false);
  readonly isSaving = signal(false);
  readonly error = signal<string | null>(null);

  readonly selected = computed<StrategyDto | null>(() => {
    const id = this.selectedId();
    if (!id) return null;
    return this.list().find(s => s.id === id) ?? null;
  });

  readonly analyticsForSelected = computed<StrategyAnalyticsDto | null>(() => {
    const id = this.selectedId();
    if (!id) return null;
    return this.analyticsById()[id] ?? null;
  });

  async loadAll(activeOnly: boolean = true): Promise<void> {
    this.isLoading.set(true);
    this.error.set(null);
    try {
      const items = await this.api.list(activeOnly);
      this.list.set(items);
    } catch (e) {
      this.error.set(this.formatError(e));
    } finally {
      this.isLoading.set(false);
    }
  }

  async create(request: UpsertStrategyRequest): Promise<StrategyDto | null> {
    this.isSaving.set(true);
    this.error.set(null);
    try {
      const saved = await this.api.create(request);
      this.list.update(items => [saved, ...items]);
      return saved;
    } catch (e) {
      this.error.set(this.formatError(e));
      return null;
    } finally {
      this.isSaving.set(false);
    }
  }

  async update(id: string, request: UpsertStrategyRequest): Promise<StrategyDto | null> {
    this.isSaving.set(true);
    this.error.set(null);
    try {
      const saved = await this.api.update(id, request);
      this.list.update(items => items.map(s => s.id === id ? saved : s));
      return saved;
    } catch (e) {
      this.error.set(this.formatError(e));
      return null;
    } finally {
      this.isSaving.set(false);
    }
  }

  async archive(id: string): Promise<boolean> {
    this.isSaving.set(true);
    this.error.set(null);
    try {
      await this.api.archive(id);
      this.list.update(items => items.filter(s => s.id !== id));
      if (this.selectedId() === id) {
        this.selectedId.set(null);
      }
      return true;
    } catch (e) {
      this.error.set(this.formatError(e));
      return false;
    } finally {
      this.isSaving.set(false);
    }
  }

  async ensureAnalytics(id: string): Promise<StrategyAnalyticsDto | null> {
    const cached = this.analyticsById()[id];
    if (cached) return cached;

    try {
      const dto = await this.api.getAnalytics(id);
      this.analyticsById.update(map => ({ ...map, [id]: dto }));
      return dto;
    } catch (e) {
      this.error.set(this.formatError(e));
      return null;
    }
  }

  reset(): void {
    this.list.set([]);
    this.selectedId.set(null);
    this.analyticsById.set({});
    this.isLoading.set(false);
    this.isSaving.set(false);
    this.error.set(null);
  }

  clearError(): void {
    this.error.set(null);
  }

  formatError(err: unknown): string {
    if (err instanceof HttpErrorResponse) {
      const body = (err.error ?? {}) as StrategyProblem;
      const detail = body.detail ?? body.title ?? err.message;
      if (detail) return detail;
      return `Error HTTP ${err.status}.`;
    }
    if (err instanceof Error && err.message) return err.message;
    return 'Ocurrió un error inesperado.';
  }
}