import { Injectable, inject, signal } from '@angular/core';
import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { firstValueFrom } from 'rxjs';

// ============================================================================
//  AccountDeletionService — Wave 11 slice 11.2b.
//
//  Signal-based wrapper around `DELETE /api/users/me/account`. The FE never
//  speaks to that endpoint directly — every consumer goes through this
//  service so:
//    - the loading/error state is centralised (signal),
//    - the wire-shape contract lives in one place,
//    - 401 / 409 / 404 responses are normalised to user-facing strings.
//
//  Auth: the endpoint requires a JWT bearer. `auth.interceptor` adds the
//  header automatically — we never set it here.
// ============================================================================

export interface DeleteAccountResponse {
  userId: string;
  softDeletedAt: string;
  scheduledHardDeleteAt: string;
  cascadeSoftDeletedRows: number;
  status: string;
}

@Injectable({ providedIn: 'root' })
export class AccountDeletionService {
  private readonly http = inject(HttpClient);

  /** `true` while the DELETE round-trip is in flight. */
  readonly loading = signal(false);

  /** Last successful response (or null until the first success). */
  readonly lastResult = signal<DeleteAccountResponse | null>(null);

  /** Last error message (or null after success / dismiss). */
  readonly error = signal<string | null>(null);

  /**
   * Schedules the GDPR Art. 17 cascade for the authenticated user.
   * Returns the response on success or throws on failure (caller handles
   * via the `error` signal + the thrown `HttpErrorResponse`).
   */
  async deleteMyAccount(): Promise<DeleteAccountResponse> {
    this.loading.set(true);
    this.error.set(null);
    try {
      const response = await firstValueFrom(
        this.http.delete<DeleteAccountResponse>('/api/users/me/account'),
      );
      this.lastResult.set(response);
      return response;
    } catch (err) {
      const message = this.toMessage(err);
      this.error.set(message);
      throw err;
    } finally {
      this.loading.set(false);
    }
  }

  /** Clears the last error so the modal can be re-opened cleanly. */
  clearError(): void {
    this.error.set(null);
  }

  private toMessage(err: unknown): string {
    if (err instanceof HttpErrorResponse) {
      if (err.status === 0) {
        return 'Sin conexión con el servidor. Verificá tu red y volvé a intentar.';
      }
      if (err.status === 401) {
        return 'Tu sesión expiró. Iniciá sesión de nuevo para continuar.';
      }
      if (err.status === 409) {
        return 'Tu cuenta ya está en proceso de eliminación.';
      }
      if (err.status === 404) {
        return 'No se encontró la cuenta. Probablemente ya fue eliminada.';
      }
      return `No se pudo programar la eliminación (HTTP ${err.status}).`;
    }
    return 'Error inesperado. Intentá de nuevo.';
  }
}
