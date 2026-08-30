import { Injectable, computed, inject, signal } from '@angular/core';
import { HttpErrorResponse } from '@angular/common/http';
import {
  BehavioralAnalysisDto,
  PatternsProblem,
  Period,
} from '../api/patterns.types';
import { PatternsService } from '../api/patterns.service';

// ============================================================================
//  PatternsState — slice 2b.2 frontend.
//
//  Signal store for the active user's behavioral analysis. The page is
//  mobile-first and renders three states from this signal set:
//   - isLoading()                  → spinner
//   - error()                      → red banner (formatError)
//   - events().length === 0 && !error() && !isLoading()
//                                  → empty-state copy
//                                  ("Sin patrones detectados en este período")
//
//  `period` lives in the state so the page can render the selector with
//  the active selection. Switching the period re-runs `load()`.
//
//  Empty payloads (events: []) are NOT errors — they are the normal
//  state for users who haven't traded yet, so the state stores them
//  silently and the page renders its empty-state copy.
// ============================================================================

@Injectable({ providedIn: 'root' })
export class PatternsState {
  private readonly api = inject(PatternsService);

  readonly events = signal<BehavioralAnalysisDto['events']>([]);
  readonly aggregations = signal<BehavioralAnalysisDto['aggregations'] | null>(
    null,
  );
  readonly period = signal<Period>('30d');
  readonly isLoading = signal(false);
  readonly error = signal<string | null>(null);

  /** The full DTO is kept for windowStart/windowEnd display. */
  readonly analysis = signal<BehavioralAnalysisDto | null>(null);

  readonly hasEvents = computed(() => this.events().length > 0);

  async load(period: Period): Promise<void> {
    this.period.set(period);
    this.isLoading.set(true);
    this.error.set(null);
    try {
      const dto = await this.api.getAnalysis(period);
      this.analysis.set(dto);
      this.events.set(dto.events);
      this.aggregations.set(dto.aggregations);
    } catch (e) {
      this.error.set(this.formatError(e));
      this.analysis.set(null);
      this.events.set([]);
      this.aggregations.set(null);
    } finally {
      this.isLoading.set(false);
    }
  }

  /** Wipe every signal back to its initial value. Used by tests and on logout. */
  reset(): void {
    this.events.set([]);
    this.aggregations.set(null);
    this.period.set('30d');
    this.isLoading.set(false);
    this.error.set(null);
    this.analysis.set(null);
  }

  clearError(): void {
    this.error.set(null);
  }

  /** Pulls a human-readable message out of any thrown value. */
  formatError(err: unknown): string {
    if (err instanceof HttpErrorResponse) {
      const body = (err.error ?? {}) as PatternsProblem;
      const detail = body.detail ?? body.title ?? err.message;
      if (detail) return detail;
      return `Error HTTP ${err.status}.`;
    }
    if (err instanceof Error && err.message) return err.message;
    return 'Ocurrió un error inesperado.';
  }
}
