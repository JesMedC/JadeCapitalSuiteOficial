import { Injectable, computed, inject, signal } from '@angular/core';
import { RiskProfileDto, RiskProfileError, UpsertRiskProfileRequest } from '../api/risk-profile.types';
import { RiskProfileService } from '../api/risk-profile.service';

// ============================================================================
//  RiskProfileState — slice 1a.2 frontend.
//
//  Signal-based store for the active risk profile. There is exactly one
//  profile per user (single-active invariant from the spec), so the state
//  keeps a single `profile` signal rather than a list.
//
//  The component decides what to render based on the signal combination:
//  - loading=true  → "Cargando perfil…"
//  - error!=null   → red banner with the normalized message
//  - profile==null → "empty state" copy (no profile yet)
//  - profile!=null → form pre-populated with the values
//
//  `saveSuccess` is a transient flag. The component flips it back to false
//  after rendering the green banner (or on next user input).
// ============================================================================

@Injectable({ providedIn: 'root' })
export class RiskProfileState {
  private readonly api = inject(RiskProfileService);

  readonly profile = signal<RiskProfileDto | null>(null);
  readonly isLoading = signal(false);
  readonly saving = signal(false);
  readonly error = signal<RiskProfileError | null>(null);
  readonly saveSuccess = signal(false);

  /** True when the state has nothing to render (no profile AND no error AND no loading). */
  readonly isEmpty = computed(() => !this.isLoading() && this.profile() === null && this.error() === null);

  /** Field-level error map keyed by form field name. Derived from the last error. */
  readonly fieldErrors = computed<Record<string, string>>(() => {
    const err = this.error();
    if (!err || err.status !== 422 || !err.field) return {};
    return { [err.field]: err.message };
  });

  async load(): Promise<void> {
    this.isLoading.set(true);
    this.error.set(null);
    this.saveSuccess.set(false);
    try {
      const profile = await this.api.getActive();
      this.profile.set(profile);
    } catch (e) {
      this.error.set(e as RiskProfileError);
    } finally {
      this.isLoading.set(false);
    }
  }

  async save(request: UpsertRiskProfileRequest): Promise<RiskProfileDto | null> {
    this.saving.set(true);
    this.error.set(null);
    this.saveSuccess.set(false);
    try {
      const saved = await this.api.upsert(request);
      this.profile.set(saved);
      this.saveSuccess.set(true);
      return saved;
    } catch (e) {
      this.error.set(e as RiskProfileError);
      return null;
    } finally {
      this.saving.set(false);
    }
  }

  clearError(): void {
    this.error.set(null);
  }

  clearSaveSuccess(): void {
    this.saveSuccess.set(false);
  }

  reset(): void {
    this.profile.set(null);
    this.isLoading.set(false);
    this.saving.set(false);
    this.error.set(null);
    this.saveSuccess.set(false);
  }
}
