import { DestroyRef, Injectable, inject, signal } from '@angular/core';
import { RiskAdviceService } from '@features/trader/risk-advice/api/risk-advice.service';
import { AiHealthDto } from '@features/trader/risk-advice/api/risk-advice.types';

// ============================================================================
//  OllamaHealthInterval — slice 5c.2 (Wave 5, E2E wiring).
//
//  Polls GET /api/ai/health every 60s via RiskAdviceService.getHealth()
//  and exposes a signal the trader-shell can render as a status badge:
//
//    'up'       — Ollama reachable, model loaded
//    'down'     — Ollama unreachable OR explicit status:down
//    'unknown'  — never polled yet (initial state)
//
//  Lifecycle:
//    - DestroyRef auto-stops the interval on host injector teardown
//      (no-op in TestBed).
//    - start() is idempotent — a second call while running is a no-op.
//
//  Defense-in-depth: the health call is wrapped in try/catch so a
//  network blip flips the badge to 'down' without crashing the app.
//
//  Pure-function extraction (resolveAiStatus) keeps the response→status
//  mapping testable in isolation; the service only orchestrates the
//  timer + signal write.
// ============================================================================

export type AiProviderStatus = 'up' | 'down' | 'unknown';

const POLL_INTERVAL_MS = 60_000;

/**
 * Pure mapping from a resolved DTO or thrown error → AiProviderStatus.
 * Extracted so tests don't have to fight fakeAsync + microtask flush
 * semantics for what is effectively a one-line conditional.
 */
export function resolveAiStatus(
  dto: AiHealthDto | null | undefined,
  error: unknown = null,
): AiProviderStatus {
  if (error !== null && error !== undefined) return 'down';
  if (!dto) return 'down';
  return dto.status === 'ok' ? 'up' : 'down';
}

@Injectable({ providedIn: 'root' })
export class OllamaHealthInterval {
  private readonly api = inject(RiskAdviceService);
  private readonly destroyRef = inject(DestroyRef, { optional: true });

  /** Current provider status. Initial value: 'unknown'. */
  readonly status = signal<AiProviderStatus>('unknown');

  private timer: ReturnType<typeof setInterval> | null = null;
  private inFlight = false;

  constructor() {
    this.destroyRef?.onDestroy(() => this.stop());
  }

  /**
   * Begin the poll. Idempotent: a second call while running is a no-op.
   *
   * `intervalMs` is an explicit test seam — production callers omit it
   * (defaults to 60s). Tests pass a small value (e.g. 25ms) so the
   * interval can be exercised in real time without fakeAsync friction.
   */
  start(intervalMs: number = POLL_INTERVAL_MS): void {
    if (this.timer !== null) return;
    this.timer = setInterval(() => void this.pollNow(), intervalMs);
  }

  /** Cancel the poll. Safe to call when not started. */
  stop(): void {
    if (this.timer !== null) {
      clearInterval(this.timer);
      this.timer = null;
    }
  }

  /** Single-shot probe. Public so consumers can force a refresh. */
  async pollNow(): Promise<void> {
    if (this.inFlight) return;
    this.inFlight = true;
    let dto: AiHealthDto | null = null;
    let error: unknown = null;
    try {
      dto = await this.api.getHealth();
    } catch (e) {
      error = e;
    } finally {
      this.inFlight = false;
    }
    this.status.set(resolveAiStatus(dto, error));
  }
}
