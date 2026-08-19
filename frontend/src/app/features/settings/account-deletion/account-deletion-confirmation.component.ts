import {
  ChangeDetectionStrategy,
  Component,
  inject,
  input,
  output,
  signal,
} from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { AccountDeletionService } from '@shared/services/account-deletion.service';

// ============================================================================
//  AccountDeletionConfirmationComponent — Wave 11 slice 11.2b.
//
//  Standalone modal that asks the user to type the literal string "ELIMINAR"
//  (case-insensitive) before the destructive `DELETE /api/users/me/account`
//  call fires. The confirmation string is the standard pattern for
//  irreversible actions — prevents foot-gun clicks.
//
//  Inputs: `authEmail` — the email shown alongside the prompt so the user
//          can verify they're deleting their own account.
//  Outputs: `confirmed` — emitted on success (parent closes the modal +
//           shows the success toast), `cancelled` — emitted on cancel.
// ============================================================================

@Component({
  selector: 'jcs-account-deletion-confirmation',
  standalone: true,
  imports: [ReactiveFormsModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="ad-modal" role="dialog" aria-labelledby="ad-modal-title" aria-modal="true">
      <div class="ad-modal__panel" data-testid="ad-confirmation-panel">
        <h2 id="ad-modal-title" class="ad-modal__title">Confirmar eliminación</h2>

        <p class="ad-modal__lead">
          Esta acción es <strong>irreversible</strong> después del período de gracia
          de 30 días. Vamos a anonimizar tu cuenta
          @if (authEmail()) {
            <strong>({{ authEmail() }})</strong>
          }
          y borrar todos los datos asociados (trades, journals, suscripciones).
        </p>

        <p class="ad-modal__warning">
          Para confirmar escribí <code>ELIMINAR</code> en mayúsculas y hacé clic en
          "Confirmar eliminación".
        </p>

        <form [formGroup]="form" (ngSubmit)="onSubmit()" data-testid="ad-confirmation-form">
          <label class="ad-modal__label" for="ad-confirm-input">
            Confirmación
          </label>
          <input
            id="ad-confirm-input"
            type="text"
            class="ad-modal__input"
            formControlName="confirmation"
            autocomplete="off"
            spellcheck="false"
            data-testid="ad-confirmation-input"
            aria-describedby="ad-confirm-hint" />

          <p id="ad-confirm-hint" class="ad-modal__hint">
            Tenés 30 días para cancelar escribiendo a soporte&#64;jadecapital.app.
          </p>

          @if (service.error(); as err) {
            <div class="ad-modal__error" role="alert" data-testid="ad-confirmation-error">
              {{ err }}
            </div>
          }

          <div class="ad-modal__actions">
            <button
              type="button"
              class="ad-btn ad-btn--ghost"
              (click)="onCancel()"
              [disabled]="service.loading()"
              data-testid="ad-confirmation-cancel">
              Cancelar
            </button>
            <button
              type="submit"
              class="ad-btn ad-btn--danger"
              [disabled]="form.invalid || service.loading()"
              data-testid="ad-confirmation-submit">
              {{ service.loading() ? 'Programando…' : 'Confirmar eliminación' }}
            </button>
          </div>
        </form>
      </div>
    </div>
  `,
  styles: [
    `
      :host { display: contents; }

      .ad-modal {
        position: fixed;
        inset: 0;
        z-index: 9999;
        display: flex;
        align-items: center;
        justify-content: center;
        background: rgba(8, 12, 20, 0.72);
        padding: var(--sp-3, 24px);
      }

      .ad-modal__panel {
        background: var(--bg-card, #161b22);
        color: var(--text-main, #e6edf3);
        border-radius: var(--radius-md, 12px);
        border: 1px solid var(--border, #30363d);
        box-shadow: 0 20px 60px rgba(0, 0, 0, 0.4);
        width: min(480px, 100%);
        padding: var(--sp-4, 28px);
      }

      .ad-modal__title {
        margin: 0 0 var(--sp-2, 12px);
        font-size: 1.25rem;
        font-weight: 600;
      }

      .ad-modal__lead { margin: 0 0 var(--sp-2, 12px); font-size: 0.95rem; line-height: 1.45; }
      .ad-modal__warning { margin: 0 0 var(--sp-3, 16px); font-size: 0.9rem; line-height: 1.45; }

      .ad-modal__label {
        display: block;
        font-size: 0.85rem;
        font-weight: 500;
        margin-bottom: var(--sp-1, 6px);
      }

      .ad-modal__input {
        width: 100%;
        padding: var(--sp-2, 10px) var(--sp-3, 12px);
        font-size: 1rem;
        background: var(--bg-input, #0d1117);
        color: var(--text-main, #e6edf3);
        border: 1px solid var(--border, #30363d);
        border-radius: var(--radius-sm, 6px);
        font-family: var(--font-mono, ui-monospace, monospace);
      }
      .ad-modal__input:focus { outline: 2px solid var(--border-active, #58a6ff); outline-offset: 1px; }

      .ad-modal__hint {
        margin: var(--sp-1, 6px) 0 0;
        font-size: 0.8rem;
        color: var(--text-muted, #8b949e);
      }

      .ad-modal__error {
        margin-top: var(--sp-2, 12px);
        padding: var(--sp-2, 10px) var(--sp-3, 12px);
        background: rgba(248, 81, 73, 0.12);
        border: 1px solid rgba(248, 81, 73, 0.4);
        border-radius: var(--radius-sm, 6px);
        color: #f85149;
        font-size: 0.85rem;
      }

      .ad-modal__actions {
        display: flex;
        gap: var(--sp-2, 10px);
        justify-content: flex-end;
        margin-top: var(--sp-3, 16px);
      }

      .ad-btn {
        padding: var(--sp-2, 10px) var(--sp-3, 14px);
        border-radius: var(--radius-sm, 6px);
        font-size: 0.9rem;
        font-weight: 500;
        border: 1px solid transparent;
        cursor: pointer;
      }
      .ad-btn:disabled { cursor: not-allowed; opacity: 0.6; }
      .ad-btn--ghost {
        background: transparent;
        color: var(--text-main, #e6edf3);
        border-color: var(--border, #30363d);
      }
      .ad-btn--danger {
        background: #da3633;
        color: #ffffff;
      }
      .ad-btn--danger:hover:not(:disabled) { background: #b62324; }
    `,
  ],
})
export class AccountDeletionConfirmationComponent {
  protected readonly service = inject(AccountDeletionService);
  private readonly fb = inject(FormBuilder);

  /** Email of the currently-authenticated user (shown in the prompt). */
  readonly authEmail = input<string | null>(null);

  readonly form = this.fb.nonNullable.group({
    confirmation: ['', [Validators.required, this.mustBeEliminarValidator]],
  });

  /** Emitted on successful DELETE — the parent should close + show toast. */
  readonly confirmed = output<void>();

  /** Emitted on cancel / backdrop click — the parent should close the modal. */
  readonly cancelled = output<void>();

  onCancel(): void {
    this.service.clearError();
    this.cancelled.emit();
  }

  async onSubmit(): Promise<void> {
    if (this.form.invalid || this.service.loading()) return;
    try {
      await this.service.deleteMyAccount();
      this.confirmed.emit();
    } catch {
      // Error already pushed into service.error() by the service; UI shows it.
    }
  }

  /** Validator: the user must type the literal "ELIMINAR" (case-insensitive). */
  private mustBeEliminarValidator(control: { value: string }): { [k: string]: boolean } | null {
    const v = (control.value ?? '').trim().toUpperCase();
    return v === 'ELIMINAR' ? null : { notEliminar: true };
  }
}
