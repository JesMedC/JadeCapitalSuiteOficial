import { computed, inject, Injectable, signal } from '@angular/core';
import { RiskAdviceService } from '../api/risk-advice.service';
import { RiskAdviceDto, RiskAdviceRequest } from '../api/risk-advice.types';

// ============================================================================
//  RiskAdviceState — slice 5c.1 (Wave 5, AI Risk Advisor Pre-Trade).
//
//  Signals-shaped state for the AI risk advisor panel. Tracks the current
//  advisory + the loading flag + the last error. The state is intentionally
//  scoped to the panel — the trade page injects it and consumes the
//  signals directly via @for / @if control flow.
// ============================================================================

@Injectable({ providedIn: 'root' })
export class RiskAdviceState {
  private readonly svc = inject(RiskAdviceService);

  readonly currentAdvice = signal<RiskAdviceDto | null>(null);
  readonly isLoading = signal(false);
  readonly error = signal<string | null>(null);

  readonly action = computed(() => this.currentAdvice()?.action ?? null);
  readonly hasAdvisory = computed(() => this.currentAdvice() !== null);
  readonly isBlock = computed(() => this.currentAdvice()?.action === 'block');
  readonly isWarning = computed(() => this.currentAdvice()?.action === 'warning');

  async requestAdvice(payload: RiskAdviceRequest): Promise<RiskAdviceDto | null> {
    this.isLoading.set(true);
    this.error.set(null);
    try {
      const advice = await this.svc.requestAdvice(payload);
      this.currentAdvice.set(advice);
      return advice;
    } catch (err: any) {
      this.error.set(this.formatError(err));
      return null;
    } finally {
      this.isLoading.set(false);
    }
  }

  async loadCachedAdvice(tradeId: string): Promise<RiskAdviceDto | null> {
    this.isLoading.set(true);
    this.error.set(null);
    try {
      const advice = await this.svc.getCachedAdvice(tradeId);
      this.currentAdvice.set(advice);
      return advice;
    } catch (err: any) {
      // 404 is a valid state (no cached advisory yet) — keep the signal null.
      this.currentAdvice.set(null);
      this.error.set(this.formatError(err));
      return null;
    } finally {
      this.isLoading.set(false);
    }
  }

  clear(): void {
    this.currentAdvice.set(null);
    this.error.set(null);
  }

  private formatError(err: any): string {
    if (err?.error?.detail) return String(err.error.detail);
    if (err?.error?.title) return String(err.error.title);
    if (err?.message) return String(err.message);
    return 'No pudimos conectar con el servidor.';
  }
}
