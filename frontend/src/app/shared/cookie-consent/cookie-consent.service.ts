import { Injectable, computed, inject, signal } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { firstValueFrom } from 'rxjs';

// ============================================================================
//  CookieConsentService — Wave 11 slice 11.4.
//
//  Signal-based wrapper around the cookie banner state. The FE persists
//  the decision to `localStorage.jade.consent` AND POSTs it to
//  `POST /api/auth/consent` so the BE has a durable audit trail (GDPR
//  Art. 7 + ePrivacy Directive 2002/58/EC art. 5(3)).
//
//  <para>
//  <b>State surface</b>:
//  <list type="bullet">
//  <item><c>choice</c>: signal<'all' | 'essential' | null> — current
//  in-memory decision (null = banner should show).</item>
//  <item><c>shouldShowBanner</c>: computed boolean — true when the user
//  has not yet chosen AND we are not already loading.</item>
//  <item><c>loading</c>: signal<boolean> — round-trip state.</item>
//  </list>
//  </para>
//
//  <para>
//  <b>Failure isolation</b>: if <c>POST /api/auth/consent</c> fails,
//  the localStorage entry is still written so the FE banner disappears
//  on subsequent loads. The BE-side audit can be reconciled by a future
//  <c>retry queue</c> or a background reconciliation job. A user who
//  has not chosen should NOT see the banner forever on every page
//  navigation.
//  </para>
// ============================================================================

const STORAGE_KEY = 'jade.consent';
export type CookieChoice = 'all' | 'essential';

export interface PersistedConsent {
  choice: CookieChoice;
  decidedAt: string; // ISO-8601
}

@Injectable({ providedIn: 'root' })
export class CookieConsentService {
  private readonly http = inject(HttpClient);

  private readonly _choice = signal<CookieChoice | null>(this.loadFromStorage()?.choice ?? null);
  private readonly _loading = signal(false);

  readonly choice = this._choice.asReadonly();
  readonly loading = this._loading.asReadonly();

  /**
   * True when the user has not yet made a decision. Components use this
   * to decide whether to render the bottom-banner shell.
   */
  readonly shouldShowBanner = computed(() => this._choice() === null);

  /**
   * True when analytics + functional cookies should be installed.
   * Components gate analytics-only scripts (Plausible, Sentry breadcrumbs,
   * etc.) on this flag.
   */
  readonly canLoadAnalytics = computed(() => this._choice() === 'all');

  /**
   * Persist the user's choice. Writes to localStorage FIRST (so the
   * banner disappears immediately on subsequent navigation) then POSTs
   * the decision to the BE for audit durability. The post is best-effort
   * — a 4xx/5xx does NOT roll back the localStorage entry because:
   *   - the user already saw the UX, and
   *   - the BE audit is a compliance copy of the FE state, not the
   *     canonical record (the FE's banner remembers either way).
   */
  async setChoice(choice: CookieChoice): Promise<void> {
    const decidedAt = new Date().toISOString();
    const persisted: PersistedConsent = { choice, decidedAt };

    this.writeToStorage(persisted);
    this._choice.set(choice);

    this._loading.set(true);
    try {
      await firstValueFrom(
        this.http.post('/api/auth/consent', { choice }),
      );
    } catch {
      // Surface the failure in `loading` clearing but keep the choice.
      // A follow-up slice could enqueue a retry.
    } finally {
      this._loading.set(false);
    }
  }

  /**
   * Test/debug seam: clears the localStorage entry + the in-memory
   * signal so a QA session can re-trigger the banner without a browser
   * refresh.
   */
  resetForTesting(): void {
    try {
      localStorage.removeItem(STORAGE_KEY);
    } catch {
      // localStorage may be unavailable (SSR / private mode); swallow.
    }
    this._choice.set(null);
  }

  private loadFromStorage(): PersistedConsent | null {
    try {
      const raw = typeof localStorage !== 'undefined'
        ? localStorage.getItem(STORAGE_KEY)
        : null;
      if (!raw) return null;
      const parsed = JSON.parse(raw) as PersistedConsent;
      if (parsed.choice !== 'all' && parsed.choice !== 'essential') return null;
      if (typeof parsed.decidedAt !== 'string') return null;
      return parsed;
    } catch {
      return null;
    }
  }

  private writeToStorage(persisted: PersistedConsent): void {
    try {
      if (typeof localStorage !== 'undefined') {
        localStorage.setItem(STORAGE_KEY, JSON.stringify(persisted));
      }
    } catch {
      // localStorage may be unavailable (private mode / SSR); swallow.
    }
  }
}
