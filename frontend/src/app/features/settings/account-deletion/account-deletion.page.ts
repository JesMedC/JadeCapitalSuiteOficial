import {
  ChangeDetectionStrategy,
  Component,
  computed,
  inject,
  signal,
} from '@angular/core';
import { DatePipe } from '@angular/common';
import { AuthState } from '@core/state/auth.state';
import { AccountDeletionConfirmationComponent } from './account-deletion-confirmation.component';
import { AccountDeletionService } from '@shared/services/account-deletion.service';

// ============================================================================
//  AccountDeletionPage — Wave 11 slice 11.2b.
//
//  Self-contained GDPR Art. 17 page. Three states:
//    1. Initial         — heading + warning + "Solicitar eliminación" button.
//    2. Confirming      — modal asks the user to type ELIMINAR.
//    3. Result          — success message + scheduled hard-delete timestamp.
//
//  The page is intentionally low-chrome (no tab shell, no sidebar) —
//  /settings/delete-account is its own URL, mounted directly under the
//  trader-shell (auth-guarded via app.routes.ts → /app).
//
//  Auth: this page is mounted under the trader-shell, which requires a
//  valid JWT. The DELETE endpoint re-validates the JWT independently —
//  the page cannot bypass it.
// ============================================================================

@Component({
  selector: 'jcs-account-deletion-page',
  standalone: true,
  imports: [DatePipe, AccountDeletionConfirmationComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <section class="ad-page" data-testid="account-deletion-page">
      <header class="ad-page__head">
        <p class="ad-page__eyebrow jcs-muted">Privacidad · GDPR Art. 17</p>
        <h1 class="ad-page__title">Eliminar mi cuenta</h1>
        <p class="ad-page__sub">
          Esta acción es <strong>irreversible</strong> después del período de gracia
          de 30 días. Vamos a anonimizar tu cuenta y todos los datos asociados
          (trades, journals, suscripciones, perfil de riesgo).
        </p>
      </header>

      @if (auth.user(); as u) {
        <article class="ad-card" data-testid="ad-account-card">
          <h2 class="ad-card__title">Cuenta actual</h2>
          <dl class="ad-card__list">
            <div><dt>Email</dt><dd>{{ u.email }}</dd></div>
            <div><dt>Display name</dt><dd>{{ u.displayName }}</dd></div>
            <div><dt>Rol</dt><dd>{{ u.role }}</dd></div>
          </dl>
        </article>
      }

      <article class="ad-card ad-card--danger" data-testid="ad-warning-card">
        <h2 class="ad-card__title">Qué pasa cuando confirmás</h2>
        <ol class="ad-card__steps">
          <li>Tu email pasa a <code>deleted-&#123;guid&#125;&#64;anonymized.local</code> y tu display name a <code>Deleted User</code>.</li>
          <li>Tu contraseña, refresh tokens y perfil de riesgo quedan revocados.</li>
          <li>Todos tus trades, journals, estrategias, alertas y suscripciones son marcados como eliminados.</li>
          <li>La fila de auditoría queda anonimizada con un hash determinístico.</li>
          <li>Después de 30 días, los datos se purgan físicamente de la base de datos.</li>
        </ol>

        @if (!result()) {
          <button
            type="button"
            class="ad-btn ad-btn--danger"
            (click)="openConfirmation()"
            [disabled]="service.loading()"
            data-testid="ad-request-button">
            Solicitar eliminación
          </button>
        }
      </article>

      @if (result(); as r) {
        <article class="ad-card ad-card--success" data-testid="ad-success-card" role="status">
          <h2 class="ad-card__title">Eliminación programada</h2>
          <p>
            Tu cuenta fue anonimizada. Tenés hasta
            <strong>{{ r.scheduledHardDeleteAt | date: 'long' }}</strong>
            para cancelar escribiendo a soporte&#64;jadecapital.app.
          </p>
          <dl class="ad-card__list">
            <div><dt>Anonimizado el</dt><dd>{{ r.softDeletedAt | date: 'long' }}</dd></div>
            <div><dt>Hard-delete programado</dt><dd>{{ r.scheduledHardDeleteAt | date: 'long' }}</dd></div>
            <div><dt>Filas afectadas por la cascada</dt><dd>{{ r.cascadeSoftDeletedRows }}</dd></div>
          </dl>
        </article>
      }

      @if (showConfirmation()) {
        <jcs-account-deletion-confirmation
          [authEmail]="authEmail()"
          (confirmed)="onConfirmed()"
          (cancelled)="closeConfirmation()" />
      }
    </section>
  `,
  styles: [
    `
      :host { display: block; }

      .ad-page {
        max-width: 720px;
        margin: 0 auto;
        padding: var(--sp-4, 28px) var(--sp-3, 18px);
        display: flex;
        flex-direction: column;
        gap: var(--sp-3, 18px);
      }

      .ad-page__head { display: flex; flex-direction: column; gap: var(--sp-1, 6px); }
      .ad-page__eyebrow { margin: 0; font-size: 0.85rem; text-transform: uppercase; letter-spacing: 0.04em; }
      .ad-page__title { margin: 0; font-size: 1.65rem; font-weight: 600; }
      .ad-page__sub { margin: 0; font-size: 0.95rem; line-height: 1.5; }

      .ad-card {
        background: var(--bg-card, #161b22);
        border: 1px solid var(--border, #30363d);
        border-radius: var(--radius-md, 12px);
        padding: var(--sp-3, 18px) var(--sp-4, 22px);
      }
      .ad-card--danger { border-color: rgba(248, 81, 73, 0.4); }
      .ad-card--success { border-color: rgba(63, 185, 80, 0.4); }

      .ad-card__title { margin: 0 0 var(--sp-2, 10px); font-size: 1.1rem; font-weight: 600; }

      .ad-card__list {
        display: grid;
        grid-template-columns: max-content 1fr;
        column-gap: var(--sp-3, 16px);
        row-gap: var(--sp-1, 6px);
        margin: 0;
      }
      .ad-card__list dt { color: var(--text-muted, #8b949e); font-size: 0.85rem; }
      .ad-card__list dd { margin: 0; font-size: 0.95rem; }

      .ad-card__steps {
        margin: 0 0 var(--sp-3, 16px);
        padding-left: 1.25rem;
        line-height: 1.6;
        font-size: 0.92rem;
      }

      .ad-btn {
        padding: var(--sp-2, 10px) var(--sp-3, 14px);
        border-radius: var(--radius-sm, 6px);
        font-size: 0.95rem;
        font-weight: 500;
        border: 1px solid transparent;
        cursor: pointer;
      }
      .ad-btn:disabled { cursor: not-allowed; opacity: 0.6; }
      .ad-btn--danger {
        background: #da3633;
        color: #ffffff;
      }
      .ad-btn--danger:hover:not(:disabled) { background: #b62324; }
    `,
  ],
})
export class AccountDeletionPage {
  protected readonly auth = inject(AuthState);
  protected readonly service = inject(AccountDeletionService);

  /** Toggles the confirmation modal. */
  readonly showConfirmation = signal(false);

  /** Latest successful response (or null). */
  readonly result = this.service.lastResult;

  readonly authEmail = computed(() => this.auth.user()?.email ?? null);

  openConfirmation(): void {
    this.service.clearError();
    this.showConfirmation.set(true);
  }

  closeConfirmation(): void {
    this.showConfirmation.set(false);
  }

  onConfirmed(): void {
    this.showConfirmation.set(false);
  }
}
