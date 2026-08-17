import { Injectable, inject, signal } from '@angular/core';
import { HttpErrorResponse } from '@angular/common/http';
import {
  AiCoachingPromptDto,
  AiCoachingPromptsDto,
  CoachingPromptsDto,
  CoachingPromptDto,
  Period,
} from '../api/coaching.types';
import { CoachingService } from '../api/coaching.service';

// ============================================================================
//  CoachingState — slice 2d.2 frontend + slice 5b.2 (AI prompts).
//
//  Signal store for the dashboard-embedded coaching prompt list. Tracks
//  BOTH the rule-based prompts (Wave 3b) and the AI-generated prompts
//  (slice 5b.2) so the page can render them in two distinct sections.
//
//  Empty payloads (prompts: []) are NOT errors — they are the normal
//  state for users who have no behavioral events (or no trades at all).
//  The page renders an "Sin prompts activos" copy in that case.
//
//  The component is "reusable" per task spec: it consumes just the
//  prompts array (signal input). Period lives here in the state because
//  the dashboard calls load('30d') once; advanced reusables can route
//  their own period knob via the state.
// ============================================================================

@Injectable({ providedIn: 'root' })
export class CoachingState {
  private readonly api = inject(CoachingService);

  // Wave 3b — rule-based prompts.
  readonly prompts = signal<CoachingPromptDto[]>([]);

  // Slice 5b.2 — AI-generated prompts.
  readonly aiPrompts = signal<AiCoachingPromptDto[]>([]);
  readonly aiIsLoading = signal(false);
  readonly aiError = signal<string | null>(null);

  readonly period = signal<Period>('30d');
  readonly isLoading = signal(false);
  readonly error = signal<string | null>(null);

  readonly hasPrompts = (): boolean => this.prompts().length > 0;
  readonly hasAiPrompts = (): boolean => this.aiPrompts().length > 0;

  async load(period: Period): Promise<void> {
    this.period.set(period);
    this.isLoading.set(true);
    this.error.set(null);
    this.aiIsLoading.set(true);
    this.aiError.set(null);
    try {
      // Load rule-based + AI prompts in parallel (slice 5b.2 — merge is
      // FE-side per design D5).
      const [rules, ai] = await Promise.all([
        this.api.getPrompts(period),
        this.api.getAiPrompts(period),
      ]);
      this.prompts.set(rules.prompts ?? []);
      this.aiPrompts.set(ai.prompts ?? []);
    } catch (e) {
      this.error.set(this.formatError(e));
      this.prompts.set([]);
      this.aiPrompts.set([]);
    } finally {
      this.isLoading.set(false);
      this.aiIsLoading.set(false);
    }
  }

  reset(): void {
    this.prompts.set([]);
    this.aiPrompts.set([]);
    this.period.set('30d');
    this.isLoading.set(false);
    this.aiIsLoading.set(false);
    this.error.set(null);
    this.aiError.set(null);
  }

  formatError(err: unknown): string {
    if (err instanceof HttpErrorResponse) {
      const body = (err.error ?? {}) as { detail?: string; title?: string };
      const detail = body?.detail ?? body?.title ?? err.message;
      if (detail) return detail;
      return `Error HTTP ${err.status}.`;
    }
    if (err instanceof Error && err.message) return err.message;
    return 'Ocurrió un error inesperado.';
  }
}
