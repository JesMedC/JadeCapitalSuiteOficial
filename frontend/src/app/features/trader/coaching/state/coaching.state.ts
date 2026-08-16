import { Injectable, inject, signal } from '@angular/core';
import { HttpErrorResponse } from '@angular/common/http';
import { CoachingPromptsDto, CoachingPromptDto, Period } from '../api/coaching.types';
import { CoachingService } from '../api/coaching.service';

// ============================================================================
//  CoachingState — slice 2d.2 frontend.
//
//  Signal store for the dashboard-embedded coaching prompt list. The
//  dashboard is the primary consumer (slice 2e wires it via the embed);
//  slice 2e+ may also surface prompts in the journal page or as a Wave 3
//  notification trigger.
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

  readonly prompts = signal<CoachingPromptDto[]>([]);
  readonly period = signal<Period>('30d');
  readonly isLoading = signal(false);
  readonly error = signal<string | null>(null);

  readonly hasPrompts = (): boolean => this.prompts().length > 0;

  async load(period: Period): Promise<void> {
    this.period.set(period);
    this.isLoading.set(true);
    this.error.set(null);
    try {
      const dto: CoachingPromptsDto = await this.api.getPrompts(period);
      this.prompts.set(dto.prompts ?? []);
    } catch (e) {
      this.error.set(this.formatError(e));
      this.prompts.set([]);
    } finally {
      this.isLoading.set(false);
    }
  }

  reset(): void {
    this.prompts.set([]);
    this.period.set('30d');
    this.isLoading.set(false);
    this.error.set(null);
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
