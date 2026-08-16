import { Injectable, computed, inject, signal } from '@angular/core';
import { HttpErrorResponse } from '@angular/common/http';
import {
  JournalEntryDto,
  JournalProblem,
  UpsertJournalEntryRequest,
} from '../api/journal.types';
import { JournalService } from '../api/journal.service';

// ============================================================================
//  JournalState — slice 2a.2 frontend.
//
//  Signal store for the active user's journal entry for "today".
//  The page is mobile-first and renders three states from this signal set:
//   - isLoading()                    → spinner
//   - error()                        → red banner (formatError)
//   - entry() == null && !error()    → empty state copy ("Aún no escribiste hoy")
//   - entry() != null                → form pre-populated with the DTO
//
//  `lastSavedAt` is a transient Date signal. The page reads it to render
//  the green "Guardado HH:mm" banner and clears it after the user starts
//  typing again (handled in the component, not here).
//
//  404 is NOT an error here — it is the "no entry yet" signal from the
//  backend. The state collapses it to `entry = null` silently so the page
//  can render its empty-state copy without a red banner.
// ============================================================================

@Injectable({ providedIn: 'root' })
export class JournalState {
  private readonly api = inject(JournalService);

  readonly entry = signal<JournalEntryDto | null>(null);
  readonly isLoading = signal(false);
  readonly isSaving = signal(false);
  readonly error = signal<string | null>(null);
  readonly lastSavedAt = signal<Date | null>(null);

  readonly hasEntry = computed(() => this.entry() !== null);

  async loadToday(): Promise<void> {
    this.isLoading.set(true);
    this.error.set(null);
    try {
      const dto = await this.api.getToday();
      this.entry.set(dto);
    } catch (e) {
      // 404 = "no entry yet" (silent). Any other status is a real error.
      if (e instanceof HttpErrorResponse && e.status === 404) {
        this.entry.set(null);
      } else {
        this.error.set(this.formatError(e));
        this.entry.set(null);
      }
    } finally {
      this.isLoading.set(false);
    }
  }

  /**
   * Upserts the entry. Returns the saved DTO on success or `null` on error.
   * On success the entry signal AND lastSavedAt are updated so the green
   * banner can render "Guardado HH:mm".
   */
  async save(request: UpsertJournalEntryRequest): Promise<JournalEntryDto | null> {
    this.isSaving.set(true);
    this.error.set(null);
    try {
      const saved = await this.api.upsertToday(request);
      this.entry.set(saved);
      this.lastSavedAt.set(new Date());
      return saved;
    } catch (e) {
      this.error.set(this.formatError(e));
      return null;
    } finally {
      this.isSaving.set(false);
    }
  }

  /** Hard-deletes today's entry. The state clears entry + lastSavedAt + error on success. */
  async remove(): Promise<boolean> {
    const current = this.entry();
    if (!current) return true;
    this.isSaving.set(true);
    this.error.set(null);
    try {
      await this.api.delete(current.id);
      this.entry.set(null);
      this.lastSavedAt.set(null);
      return true;
    } catch (e) {
      this.error.set(this.formatError(e));
      return false;
    } finally {
      this.isSaving.set(false);
    }
  }

  /** Wipe every signal back to its initial value. Used by tests and on logout. */
  reset(): void {
    this.entry.set(null);
    this.isLoading.set(false);
    this.isSaving.set(false);
    this.error.set(null);
    this.lastSavedAt.set(null);
  }

  clearError(): void {
    this.error.set(null);
  }

  /** Pulls a human-readable message out of any thrown value. */
  formatError(err: unknown): string {
    if (err instanceof HttpErrorResponse) {
      const body = (err.error ?? {}) as JournalProblem;
      const detail = body.detail ?? body.title ?? err.message;
      if (detail) return detail;
      return `Error HTTP ${err.status}.`;
    }
    if (err instanceof Error && err.message) return err.message;
    return 'Ocurrió un error inesperado.';
  }
}
