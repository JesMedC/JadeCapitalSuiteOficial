import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { DecimalPipe, NgClass } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { AccountApiService, AccountDto } from '@core/api/account-api.service';
import {
  ASSET_CLASS_LABELS,
  AssetClass,
  InstrumentApiService,
  InstrumentDto,
} from '@core/api/instrument-api.service';
import { AuthState } from '@core/state/auth.state';

type SettingsTab = 'accounts' | 'instruments';
type DeleteKind = 'account' | 'instrument';

interface DeleteTarget {
  kind: DeleteKind;
  id: string;
  label: string;
}

@Component({
  selector: 'jcs-settings',
  standalone: true,
  imports: [DecimalPipe, NgClass, ReactiveFormsModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="settings-page">
      <header class="page-head">
        <div>
          <p class="jcs-muted page-eyebrow">Workspace de {{ auth.user()?.displayName || auth.user()?.email }}</p>
          <h1 class="page-title">Configuración</h1>
          <p class="jcs-muted page-subtitle">Administrá tus cuentas de trading y el catálogo de instrumentos.</p>
        </div>
      </header>

      <div class="tabs" role="tablist" aria-label="Secciones de configuración">
        <button
          type="button"
          class="tab-pill"
          [class.tab-pill--active]="activeTab() === 'accounts'"
          [attr.aria-selected]="activeTab() === 'accounts'"
          role="tab"
          (click)="setTab('accounts')">
          Cuentas
          <span class="tab-count jcs-num">{{ accounts().length }}</span>
        </button>
        <button
          type="button"
          class="tab-pill"
          [class.tab-pill--active]="activeTab() === 'instruments'"
          [attr.aria-selected]="activeTab() === 'instruments'"
          role="tab"
          (click)="setTab('instruments')">
          Instrumentos
          <span class="tab-count jcs-num">{{ instruments().length }}</span>
        </button>
      </div>

      @if (activeTab() === 'accounts') {
        <section class="tab-panel" role="tabpanel">
          <header class="section-head">
            <div>
              <h2>Cuentas</h2>
              <p class="jcs-muted">{{ activeAccountsCount() }} activas de {{ accounts().length }} cuentas.</p>
            </div>
            <button type="button" class="jcs-btn jcs-btn--primary" (click)="openNewAccount()">
              + Nueva cuenta
            </button>
          </header>

          @if (accountError()) {
            <div class="error-banner" role="alert">
              <span>No se pudieron cargar las cuentas: {{ accountError() }}</span>
              <button type="button" class="error-retry" (click)="loadAccounts()">Reintentar</button>
            </div>
          }

          @if (accountLoading()) {
            <div class="loading-grid" aria-label="Cargando cuentas">
              @for (item of [1, 2, 3]; track item) {
                <div class="jcs-card skeleton-card">
                  <span class="skeleton skeleton--title"></span>
                  <span class="skeleton skeleton--line"></span>
                  <span class="skeleton skeleton--line skeleton--short"></span>
                </div>
              }
            </div>
          } @else if (accounts().length === 0 && !accountError()) {
            <div class="jcs-card empty-state">
              <span class="empty-icon" aria-hidden="true">$</span>
              <h3>No tenés cuentas creadas todavía</h3>
              <p class="jcs-muted">Creá una cuenta para empezar a registrar tu actividad de trading.</p>
              <button type="button" class="jcs-btn jcs-btn--primary" (click)="openNewAccount()">Crear cuenta</button>
            </div>
          } @else {
            <div class="entity-grid">
              @for (account of accounts(); track account.id) {
                <article class="jcs-card entity-card" [class.entity-card--inactive]="!account.isActive">
                  <header class="entity-head">
                    <span class="entity-icon" aria-hidden="true">{{ currencyIcon(account.currency) }}</span>
                    <div class="entity-title">
                      <h3>{{ account.name }}</h3>
                      <p class="jcs-muted">{{ account.broker }}</p>
                    </div>
                    <span class="jcs-badge" [ngClass]="account.isActive ? 'status-active' : 'status-inactive'">
                      {{ account.isActive ? 'Activa' : 'Inactiva' }}
                    </span>
                  </header>

                  <dl class="entity-stats">
                    <div>
                      <dt>Balance inicial</dt>
                      <dd class="jcs-num">{{ account.initialBalance | number:'1.2-2' }} {{ account.currency }}</dd>
                    </div>
                    <div>
                      <dt>Leverage</dt>
                      <dd class="jcs-num">1:{{ account.leverage | number:'1.0-2' }}</dd>
                    </div>
                    <div>
                      <dt>Payout</dt>
                      <dd class="jcs-num">{{ account.payoutPercent * 100 | number:'1.0-2' }}%</dd>
                    </div>
                    <div>
                      <dt>Moneda</dt>
                      <dd class="jcs-num">{{ account.currency }}</dd>
                    </div>
                  </dl>

                  <footer class="entity-actions">
                    <button type="button" class="jcs-btn jcs-btn--ghost jcs-btn--sm" (click)="editAccount(account)">Editar</button>
                    <button
                      type="button"
                      class="jcs-btn jcs-btn--ghost jcs-btn--sm"
                      [disabled]="actionId() === account.id"
                      (click)="toggleAccount(account)">
                      {{ actionId() === account.id ? 'Guardando…' : (account.isActive ? 'Desactivar' : 'Activar') }}
                    </button>
                    <button type="button" class="danger-btn" (click)="askDelete('account', account.id, account.name)">Eliminar</button>
                  </footer>
                </article>
              }
            </div>
          }

          @if (accountFormOpen()) {
            <form class="jcs-card jcs-card--glow inline-form" [formGroup]="accountForm" (ngSubmit)="submitAccount()">
              <header class="form-head">
                <div>
                  <h3>{{ accountFormTitle() }}</h3>
                  <p class="jcs-muted">Los porcentajes se ingresan entre 0 y 100.</p>
                </div>
                <button type="button" class="close-btn" (click)="closeAccountForm()" aria-label="Cerrar formulario">×</button>
              </header>

              <div class="form-grid">
                <div class="field">
                  <label class="jcs-label" for="account-name">Nombre</label>
                  <input id="account-name" class="jcs-input" formControlName="name" maxlength="80" placeholder="Cuenta principal"
                    [class.jcs-input--error]="isInvalid(accountForm.controls.name)">
                  @if (isInvalid(accountForm.controls.name)) { <span class="field-error">Ingresá un nombre de hasta 80 caracteres.</span> }
                </div>
                <div class="field">
                  <label class="jcs-label" for="account-broker">Broker</label>
                  <input id="account-broker" class="jcs-input" formControlName="broker" maxlength="80" placeholder="Nombre del broker"
                    [class.jcs-input--error]="isInvalid(accountForm.controls.broker)">
                  @if (isInvalid(accountForm.controls.broker)) { <span class="field-error">Ingresá un broker de hasta 80 caracteres.</span> }
                </div>
                <div class="field">
                  <label class="jcs-label" for="account-currency">Moneda</label>
                  <input id="account-currency" class="jcs-input uppercase" formControlName="currency" maxlength="3" placeholder="USD"
                    [class.jcs-input--error]="isInvalid(accountForm.controls.currency)">
                  @if (isInvalid(accountForm.controls.currency)) { <span class="field-error">Usá un código ISO de 3 letras.</span> }
                </div>
                <div class="field">
                  <label class="jcs-label" for="account-balance">Balance inicial</label>
                  <input id="account-balance" class="jcs-input jcs-num" type="number" min="0" step="0.01" formControlName="initialBalance"
                    [class.jcs-input--error]="isInvalid(accountForm.controls.initialBalance)">
                  @if (editingAccount()) { <span class="field-hint">El balance inicial no puede cambiarse después de crear la cuenta.</span> }
                  @else if (isInvalid(accountForm.controls.initialBalance)) { <span class="field-error">El balance debe ser 0 o mayor.</span> }
                </div>
                <div class="field">
                  <label class="jcs-label" for="account-leverage">Leverage</label>
                  <input id="account-leverage" class="jcs-input jcs-num" type="number" min="0.01" step="0.01" formControlName="leverage"
                    [class.jcs-input--error]="isInvalid(accountForm.controls.leverage)">
                  @if (isInvalid(accountForm.controls.leverage)) { <span class="field-error">El leverage debe ser mayor a 0.</span> }
                </div>
                <div class="field">
                  <label class="jcs-label" for="account-payout">Payout (%)</label>
                  <input id="account-payout" class="jcs-input jcs-num" type="number" min="0" max="100" step="0.01" formControlName="payoutPercent"
                    [class.jcs-input--error]="isInvalid(accountForm.controls.payoutPercent)">
                  @if (isInvalid(accountForm.controls.payoutPercent)) { <span class="field-error">El payout debe estar entre 0 y 100.</span> }
                </div>
              </div>

              @if (accountFormError()) { <div class="form-error" role="alert">{{ accountFormError() }}</div> }

              <footer class="form-actions">
                <button type="button" class="jcs-btn jcs-btn--ghost" (click)="closeAccountForm()">Cancelar</button>
                <button type="submit" class="jcs-btn jcs-btn--primary" [disabled]="saving()">
                  {{ saving() ? 'Guardando…' : (editingAccount() ? 'Guardar cambios' : 'Crear cuenta') }}
                </button>
              </footer>
            </form>
          }
        </section>
      } @else {
        <section class="tab-panel" role="tabpanel">
          <header class="section-head">
            <div>
              <h2>Instrumentos</h2>
              <p class="jcs-muted">{{ activeInstrumentsCount() }} activos de {{ instruments().length }} instrumentos.</p>
            </div>
            <button type="button" class="jcs-btn jcs-btn--primary" (click)="openNewInstrument()">
              + Nuevo instrumento
            </button>
          </header>

          @if (instrumentError()) {
            <div class="error-banner" role="alert">
              <span>No se pudieron cargar los instrumentos: {{ instrumentError() }}</span>
              <button type="button" class="error-retry" (click)="loadInstruments()">Reintentar</button>
            </div>
          }

          @if (instrumentLoading()) {
            <div class="loading-grid" aria-label="Cargando instrumentos">
              @for (item of [1, 2, 3]; track item) {
                <div class="jcs-card skeleton-card">
                  <span class="skeleton skeleton--title"></span>
                  <span class="skeleton skeleton--line"></span>
                  <span class="skeleton skeleton--line skeleton--short"></span>
                </div>
              }
            </div>
          } @else if (instruments().length === 0 && !instrumentError()) {
            <div class="jcs-card empty-state">
              <span class="empty-icon empty-icon--instrument" aria-hidden="true">↗</span>
              <h3>No hay instrumentos configurados</h3>
              <p class="jcs-muted">Creá el primer instrumento para habilitarlo en tus operaciones.</p>
              <button type="button" class="jcs-btn jcs-btn--primary" (click)="openNewInstrument()">Crear instrumento</button>
            </div>
          } @else {
            <div class="entity-grid">
              @for (instrument of instruments(); track instrument.id) {
                <article class="jcs-card entity-card" [class.entity-card--inactive]="!instrument.isActive">
                  <header class="entity-head">
                    <span class="entity-icon" [ngClass]="'asset-' + instrument.assetClass" aria-hidden="true">
                      {{ assetIcon(instrument.assetClass) }}
                    </span>
                    <div class="entity-title">
                      <h3 class="jcs-num">{{ instrument.symbol }}</h3>
                      <span class="jcs-badge asset-badge">{{ assetClassLabel(instrument.assetClass) }}</span>
                    </div>
                    <span class="jcs-badge" [ngClass]="instrument.isActive ? 'status-active' : 'status-inactive'">
                      {{ instrument.isActive ? 'Activo' : 'Inactivo' }}
                    </span>
                  </header>

                  <dl class="entity-stats instrument-stats">
                    <div>
                      <dt>Contrato</dt>
                      <dd class="jcs-num">{{ instrument.contractSize | number:'1.0-4' }}</dd>
                    </div>
                    <div>
                      <dt>Decimales</dt>
                      <dd class="jcs-num">{{ instrument.decimalPlaces }}</dd>
                    </div>
                    <div>
                      <dt>Valor pip</dt>
                      <dd class="jcs-num">{{ instrument.pipValue | number:'1.0-6' }}</dd>
                    </div>
                    <div>
                      <dt>Payout</dt>
                      <dd class="jcs-num">{{ instrument.payoutPercent * 100 | number:'1.0-2' }}%</dd>
                    </div>
                  </dl>

                  <footer class="entity-actions">
                    <button type="button" class="jcs-btn jcs-btn--ghost jcs-btn--sm" (click)="editInstrument(instrument)">Editar</button>
                    @if (instrument.isActive) {
                      <button
                        type="button"
                        class="jcs-btn jcs-btn--ghost jcs-btn--sm"
                        [disabled]="actionId() === instrument.id"
                        (click)="deactivateInstrument(instrument)">
                        {{ actionId() === instrument.id ? 'Guardando…' : 'Desactivar' }}
                      </button>
                    }
                    <button type="button" class="danger-btn" (click)="askDelete('instrument', instrument.id, instrument.symbol)">Eliminar</button>
                  </footer>
                </article>
              }
            </div>
          }

          @if (instrumentFormOpen()) {
            <form class="jcs-card jcs-card--glow inline-form" [formGroup]="instrumentForm" (ngSubmit)="submitInstrument()">
              <header class="form-head">
                <div>
                  <h3>{{ instrumentFormTitle() }}</h3>
                  <p class="jcs-muted">Definí las propiedades usadas para calcular y presentar operaciones.</p>
                </div>
                <button type="button" class="close-btn" (click)="closeInstrumentForm()" aria-label="Cerrar formulario">×</button>
              </header>

              <div class="form-grid">
                <div class="field">
                  <label class="jcs-label" for="instrument-symbol">Símbolo</label>
                  <input id="instrument-symbol" class="jcs-input uppercase" formControlName="symbol" maxlength="20" placeholder="EUR/USD"
                    [class.jcs-input--error]="isInvalid(instrumentForm.controls.symbol)">
                  @if (isInvalid(instrumentForm.controls.symbol)) { <span class="field-error">Usá entre 3 y 20 letras, números o “/”.</span> }
                </div>
                <div class="field">
                  <label class="jcs-label" for="instrument-asset">Clase de activo</label>
                  <select id="instrument-asset" class="jcs-input" formControlName="assetClass">
                    @for (asset of assetClasses; track asset.value) {
                      <option [ngValue]="asset.value">{{ asset.label }}</option>
                    }
                  </select>
                </div>
                <div class="field">
                  <label class="jcs-label" for="instrument-contract">Tamaño de contrato</label>
                  <input id="instrument-contract" class="jcs-input jcs-num" type="number" min="0.000001" step="any" formControlName="contractSize"
                    [class.jcs-input--error]="isInvalid(instrumentForm.controls.contractSize)">
                  @if (isInvalid(instrumentForm.controls.contractSize)) { <span class="field-error">El tamaño debe ser mayor a 0.</span> }
                </div>
                <div class="field">
                  <label class="jcs-label" for="instrument-decimals">Decimales</label>
                  <input id="instrument-decimals" class="jcs-input jcs-num" type="number" min="0" step="1" formControlName="decimalPlaces"
                    [class.jcs-input--error]="isInvalid(instrumentForm.controls.decimalPlaces)">
                  @if (isInvalid(instrumentForm.controls.decimalPlaces)) { <span class="field-error">Ingresá un número entero igual o mayor a 0.</span> }
                </div>
                <div class="field">
                  <label class="jcs-label" for="instrument-pip">Valor del pip</label>
                  <input id="instrument-pip" class="jcs-input jcs-num" type="number" min="0" step="any" formControlName="pipValue"
                    [class.jcs-input--error]="isInvalid(instrumentForm.controls.pipValue)">
                  @if (isInvalid(instrumentForm.controls.pipValue)) { <span class="field-error">El valor del pip debe ser 0 o mayor.</span> }
                </div>
                <div class="field">
                  <label class="jcs-label" for="instrument-payout">Payout (%)</label>
                  <input id="instrument-payout" class="jcs-input jcs-num" type="number" min="0" max="100" step="0.01" formControlName="payoutPercent"
                    [class.jcs-input--error]="isInvalid(instrumentForm.controls.payoutPercent)">
                  @if (isInvalid(instrumentForm.controls.payoutPercent)) { <span class="field-error">El payout debe estar entre 0 y 100.</span> }
                </div>
              </div>

              @if (instrumentFormError()) { <div class="form-error" role="alert">{{ instrumentFormError() }}</div> }

              <footer class="form-actions">
                <button type="button" class="jcs-btn jcs-btn--ghost" (click)="closeInstrumentForm()">Cancelar</button>
                <button type="submit" class="jcs-btn jcs-btn--primary" [disabled]="saving()">
                  {{ saving() ? 'Guardando…' : (editingInstrument() ? 'Guardar cambios' : 'Crear instrumento') }}
                </button>
              </footer>
            </form>
          }
        </section>
      }
    </div>

    @if (deleteTarget()) {
      <div class="modal-backdrop" role="presentation" (click)="closeDelete()">
        <section class="jcs-card delete-modal" role="alertdialog" aria-modal="true" aria-labelledby="delete-title" (click)="$event.stopPropagation()">
          <span class="delete-icon" aria-hidden="true">!</span>
          <h2 id="delete-title">Confirmar eliminación</h2>
          <p>Vas a eliminar <strong>{{ deleteTarget()?.label }}</strong>. Esta acción no se puede deshacer.</p>
          <p class="jcs-muted">La operación puede fallar si existen trades asociados.</p>
          @if (deleteError()) { <div class="form-error" role="alert">{{ deleteError() }}</div> }
          <footer class="form-actions">
            <button type="button" class="jcs-btn jcs-btn--ghost" (click)="closeDelete()" [disabled]="deleting()">Cancelar</button>
            <button type="button" class="jcs-btn delete-confirm" (click)="confirmDelete()" [disabled]="deleting()">
              {{ deleting() ? 'Eliminando…' : 'Eliminar definitivamente' }}
            </button>
          </footer>
        </section>
      </div>
    }
  `,
  styles: [`
    :host { display: block; }

    .settings-page {
      display: flex;
      flex-direction: column;
      gap: var(--sp-6);
      animation: fade-up 0.4s ease-out;
    }

    .page-head {
      display: flex;
      justify-content: space-between;
      align-items: flex-end;
      gap: var(--sp-4);
      flex-wrap: wrap;
    }
    .page-eyebrow {
      margin: 0 0 var(--sp-1);
      font-family: var(--font-mono);
      font-size: var(--fs-sm);
    }
    .page-title {
      margin: 0;
      font-size: var(--fs-3xl);
      font-weight: 700;
      letter-spacing: -0.03em;
    }
    .page-subtitle {
      margin: var(--sp-2) 0 0;
      font-size: var(--fs-sm);
    }

    .tabs {
      display: inline-flex;
      align-self: flex-start;
      gap: var(--sp-1);
      padding: var(--sp-1);
      background: var(--bg-card-soft);
      border: 1px solid var(--border-soft);
      border-radius: var(--radius-pill, 999px);
    }
    .tab-pill {
      display: inline-flex;
      align-items: center;
      gap: var(--sp-2);
      padding: var(--sp-2) var(--sp-4);
      border: 1px solid transparent;
      border-radius: var(--radius-pill, 999px);
      background: transparent;
      color: var(--text-secondary);
      font: inherit;
      font-size: var(--fs-sm);
      font-weight: 600;
      cursor: pointer;
      transition: all 150ms;
    }
    .tab-pill:hover { color: var(--text-main); }
    .tab-pill--active {
      background: var(--green-soft);
      border-color: var(--border-active);
      color: var(--green);
    }
    .tab-count {
      min-width: 20px;
      padding: 1px var(--sp-2);
      border-radius: 999px;
      background: var(--bg-elevated);
      color: var(--text-muted);
      font-size: 0.65rem;
      text-align: center;
    }

    .tab-panel {
      display: flex;
      flex-direction: column;
      gap: var(--sp-5);
    }
    .section-head {
      display: flex;
      justify-content: space-between;
      align-items: center;
      gap: var(--sp-4);
      flex-wrap: wrap;
    }
    .section-head h2 { margin: 0 0 var(--sp-1); font-size: var(--fs-xl); }
    .section-head p { margin: 0; font-size: var(--fs-sm); }

    .error-banner {
      display: flex;
      align-items: center;
      justify-content: space-between;
      gap: var(--sp-3);
      padding: var(--sp-3) var(--sp-4);
      background: rgba(255, 64, 87, 0.08);
      border: 1px solid rgba(255, 64, 87, 0.35);
      border-radius: var(--radius-md);
      color: var(--red);
      font-size: var(--fs-sm);
    }
    .error-retry {
      padding: var(--sp-1) var(--sp-3);
      border: 1px solid var(--red);
      border-radius: var(--radius-sm);
      background: transparent;
      color: var(--red);
      font: inherit;
      font-size: var(--fs-xs);
      font-weight: 600;
      cursor: pointer;
    }
    .error-retry:hover { background: rgba(255, 64, 87, 0.15); }

    .loading-grid,
    .entity-grid {
      display: grid;
      grid-template-columns: repeat(2, minmax(0, 1fr));
      gap: var(--sp-4);
    }
    .skeleton-card {
      display: flex;
      flex-direction: column;
      gap: var(--sp-3);
      min-height: 210px;
      padding: var(--sp-5);
    }
    .skeleton {
      display: block;
      height: 14px;
      border-radius: var(--radius-sm);
      background: linear-gradient(90deg, var(--bg-card-soft) 20%, var(--bg-hover) 50%, var(--bg-card-soft) 80%);
      background-size: 200% 100%;
      animation: shimmer 1.4s linear infinite;
    }
    .skeleton--title { width: 48%; height: 24px; }
    .skeleton--line { width: 82%; }
    .skeleton--short { width: 62%; }

    .empty-state {
      display: flex;
      flex-direction: column;
      align-items: center;
      justify-content: center;
      gap: var(--sp-3);
      min-height: 280px;
      padding: var(--sp-8) var(--sp-4);
      text-align: center;
    }
    .empty-state h3 { margin: 0; font-size: var(--fs-xl); }
    .empty-state p { max-width: 480px; margin: 0; font-size: var(--fs-sm); }
    .empty-icon,
    .entity-icon {
      display: inline-flex;
      align-items: center;
      justify-content: center;
      border: 1px solid var(--border-active);
      background: var(--green-soft);
      color: var(--green);
      font-family: var(--font-mono);
      font-weight: 700;
    }
    .empty-icon { width: 54px; height: 54px; border-radius: 50%; font-size: var(--fs-xl); }
    .empty-icon--instrument { border-radius: var(--radius-md); }

    .entity-card {
      display: flex;
      flex-direction: column;
      gap: var(--sp-5);
      padding: var(--sp-5);
      transition: transform 200ms ease, border-color 200ms ease, box-shadow 200ms ease;
    }
    .entity-card:hover {
      transform: translateY(-2px);
      border-color: var(--border-active);
      box-shadow: var(--shadow-soft);
    }
    .entity-card--inactive { opacity: 0.68; }
    .entity-card--inactive:hover { opacity: 0.9; }
    .entity-head {
      display: flex;
      align-items: center;
      gap: var(--sp-3);
    }
    .entity-icon {
      width: 42px;
      height: 42px;
      border-radius: var(--radius-sm);
      flex-shrink: 0;
    }
    .entity-title { flex: 1; min-width: 0; }
    .entity-title h3 {
      margin: 0 0 2px;
      overflow: hidden;
      color: var(--text-main);
      font-size: var(--fs-lg);
      text-overflow: ellipsis;
      white-space: nowrap;
    }
    .entity-title p { margin: 0; font-size: var(--fs-xs); }
    .entity-title .asset-badge { font-size: 0.65rem; }
    .status-active { background: rgba(47, 219, 120, 0.14); color: var(--green); }
    .status-inactive { background: var(--bg-elevated); color: var(--text-muted); }
    .asset-badge { background: rgba(74, 168, 255, 0.12); color: var(--blue); }
    .entity-icon.asset-2 { background: rgba(245, 183, 66, 0.1); color: var(--yellow, #f5b742); border-color: rgba(245, 183, 66, 0.3); }
    .entity-icon.asset-3 { background: rgba(74, 168, 255, 0.1); color: var(--blue); border-color: rgba(74, 168, 255, 0.3); }
    .entity-icon.asset-4 { background: rgba(229, 191, 92, 0.1); color: #e5bf5c; border-color: rgba(229, 191, 92, 0.3); }
    .entity-icon.asset-5 { background: var(--bg-elevated); color: var(--text-secondary); border-color: var(--border); }

    .entity-stats {
      display: grid;
      grid-template-columns: repeat(2, minmax(0, 1fr));
      gap: var(--sp-3);
      margin: 0;
    }
    .entity-stats div {
      display: flex;
      flex-direction: column;
      gap: var(--sp-1);
      padding: var(--sp-3);
      border-radius: var(--radius-sm);
      background: var(--bg-card-soft);
      border-left: 2px solid var(--border-active);
    }
    .entity-stats dt {
      color: var(--text-muted);
      font-size: 0.65rem;
      font-weight: 600;
      letter-spacing: 0.07em;
      text-transform: uppercase;
    }
    .entity-stats dd { margin: 0; color: var(--text-main); font-size: var(--fs-sm); font-weight: 600; }

    .entity-actions {
      display: flex;
      align-items: center;
      gap: var(--sp-2);
      padding-top: var(--sp-4);
      border-top: 1px solid var(--border-soft);
      flex-wrap: wrap;
    }
    .danger-btn,
    .delete-confirm {
      border: 1px solid rgba(255, 64, 87, 0.4);
      background: rgba(255, 64, 87, 0.08);
      color: var(--red);
    }
    .danger-btn {
      padding: var(--sp-2) var(--sp-3);
      border-radius: var(--radius-sm);
      font: inherit;
      font-size: var(--fs-xs);
      font-weight: 600;
      cursor: pointer;
    }
    .danger-btn:hover,
    .delete-confirm:hover:not(:disabled) { background: rgba(255, 64, 87, 0.16); }

    .inline-form {
      display: flex;
      flex-direction: column;
      gap: var(--sp-5);
      padding: var(--sp-6);
      animation: form-expand 220ms ease-out;
    }
    .form-head {
      display: flex;
      justify-content: space-between;
      align-items: flex-start;
      gap: var(--sp-4);
    }
    .form-head h3 { margin: 0 0 var(--sp-1); font-size: var(--fs-xl); }
    .form-head p { margin: 0; font-size: var(--fs-sm); }
    .close-btn {
      width: 34px;
      height: 34px;
      border: 1px solid var(--border);
      border-radius: var(--radius-sm);
      background: transparent;
      color: var(--text-muted);
      font-size: var(--fs-xl);
      line-height: 1;
      cursor: pointer;
    }
    .close-btn:hover { border-color: var(--border-active); color: var(--text-main); }
    .form-grid {
      display: grid;
      grid-template-columns: repeat(3, minmax(0, 1fr));
      gap: var(--sp-4);
    }
    .field { display: flex; flex-direction: column; gap: var(--sp-2); }
    .field .jcs-label { margin: 0; }
    .uppercase { text-transform: uppercase; }
    .field-error { color: var(--red); font-size: var(--fs-xs); }
    .field-hint { color: var(--text-muted); font-size: var(--fs-xs); }
    .form-error {
      padding: var(--sp-3);
      border: 1px solid rgba(255, 64, 87, 0.3);
      border-radius: var(--radius-sm);
      background: rgba(255, 64, 87, 0.08);
      color: var(--red);
      font-size: var(--fs-sm);
    }
    .form-actions {
      display: flex;
      justify-content: flex-end;
      gap: var(--sp-3);
      flex-wrap: wrap;
    }

    .modal-backdrop {
      position: fixed;
      inset: 0;
      z-index: 100;
      display: grid;
      place-items: center;
      padding: var(--sp-4);
      background: rgba(2, 7, 11, 0.78);
      backdrop-filter: blur(5px);
    }
    .delete-modal {
      display: flex;
      flex-direction: column;
      align-items: center;
      gap: var(--sp-3);
      width: min(100%, 480px);
      padding: var(--sp-7);
      text-align: center;
      animation: modal-in 180ms ease-out;
    }
    .delete-modal h2 { margin: 0; font-size: var(--fs-xl); }
    .delete-modal p { margin: 0; font-size: var(--fs-sm); }
    .delete-modal .form-actions { width: 100%; margin-top: var(--sp-3); }
    .delete-modal .form-error { width: 100%; }
    .delete-icon {
      display: inline-flex;
      align-items: center;
      justify-content: center;
      width: 48px;
      height: 48px;
      border: 1px solid rgba(255, 64, 87, 0.4);
      border-radius: 50%;
      background: rgba(255, 64, 87, 0.1);
      color: var(--red);
      font-size: var(--fs-xl);
      font-weight: 700;
    }

    @media (max-width: 960px) {
      .form-grid { grid-template-columns: repeat(2, minmax(0, 1fr)); }
    }
    @media (max-width: 720px) {
      .loading-grid,
      .entity-grid,
      .form-grid { grid-template-columns: 1fr; }
      .section-head { align-items: stretch; }
      .section-head .jcs-btn { width: 100%; }
      .tabs { width: 100%; }
      .tab-pill { flex: 1; justify-content: center; }
      .inline-form { padding: var(--sp-5); }
      .error-banner { align-items: flex-start; flex-direction: column; }
      .entity-actions .jcs-btn,
      .entity-actions .danger-btn { flex: 1; }
    }
    @media (max-width: 440px) {
      .entity-stats { grid-template-columns: 1fr; }
      .form-actions { flex-direction: column-reverse; }
      .form-actions .jcs-btn { width: 100%; }
    }

    @keyframes fade-up {
      from { opacity: 0; transform: translateY(8px); }
      to { opacity: 1; transform: translateY(0); }
    }
    @keyframes form-expand {
      from { opacity: 0; transform: translateY(-8px); }
      to { opacity: 1; transform: translateY(0); }
    }
    @keyframes modal-in {
      from { opacity: 0; transform: scale(0.96); }
      to { opacity: 1; transform: scale(1); }
    }
    @keyframes shimmer {
      from { background-position: 200% 0; }
      to { background-position: -200% 0; }
    }
  `],
})
export class SettingsPage {
  readonly auth = inject(AuthState);
  private readonly accountApi = inject(AccountApiService);
  private readonly instrumentApi = inject(InstrumentApiService);
  private readonly fb = inject(FormBuilder);

  readonly activeTab = signal<SettingsTab>('accounts');
  readonly accounts = signal<AccountDto[]>([]);
  readonly instruments = signal<InstrumentDto[]>([]);
  readonly accountLoading = signal(true);
  readonly instrumentLoading = signal(true);
  readonly accountError = signal<string | null>(null);
  readonly instrumentError = signal<string | null>(null);
  readonly accountFormOpen = signal(false);
  readonly instrumentFormOpen = signal(false);
  readonly editingAccount = signal<AccountDto | null>(null);
  readonly editingInstrument = signal<InstrumentDto | null>(null);
  readonly accountFormError = signal<string | null>(null);
  readonly instrumentFormError = signal<string | null>(null);
  readonly saving = signal(false);
  readonly actionId = signal<string | null>(null);
  readonly deleteTarget = signal<DeleteTarget | null>(null);
  readonly deleteError = signal<string | null>(null);
  readonly deleting = signal(false);

  readonly activeAccountsCount = computed(() => this.accounts().filter(account => account.isActive).length);
  readonly activeInstrumentsCount = computed(() => this.instruments().filter(instrument => instrument.isActive).length);
  readonly accountFormTitle = computed(() => this.editingAccount() ? 'Editar cuenta' : 'Nueva cuenta');
  readonly instrumentFormTitle = computed(() => this.editingInstrument() ? 'Editar instrumento' : 'Nuevo instrumento');

  readonly assetClasses: ReadonlyArray<{ value: AssetClass; label: string }> = [
    { value: 1, label: ASSET_CLASS_LABELS[1] },
    { value: 2, label: ASSET_CLASS_LABELS[2] },
    { value: 3, label: ASSET_CLASS_LABELS[3] },
    { value: 4, label: ASSET_CLASS_LABELS[4] },
    { value: 5, label: ASSET_CLASS_LABELS[5] },
  ];

  readonly accountForm = this.fb.nonNullable.group({
    name: ['', [Validators.required, Validators.maxLength(80)]],
    broker: ['', [Validators.required, Validators.maxLength(80)]],
    currency: ['USD', [Validators.required, Validators.pattern(/^[A-Za-z]{3}$/)]],
    initialBalance: [0, [Validators.required, Validators.min(0)]],
    leverage: [1, [Validators.required, Validators.min(0.01)]],
    payoutPercent: [0, [Validators.required, Validators.min(0), Validators.max(100)]],
  });

  readonly instrumentForm = this.fb.nonNullable.group({
    symbol: ['', [Validators.required, Validators.minLength(3), Validators.maxLength(20), Validators.pattern(/^[A-Za-z0-9/]+$/)]],
    assetClass: [1 as AssetClass, [Validators.required]],
    contractSize: [1, [Validators.required, Validators.min(0.000001)]],
    decimalPlaces: [2, [Validators.required, Validators.min(0), Validators.pattern(/^\d+$/)]],
    pipValue: [0, [Validators.required, Validators.min(0)]],
    payoutPercent: [0, [Validators.required, Validators.min(0), Validators.max(100)]],
  });

  constructor() {
    void this.loadAccounts();
    void this.loadInstruments();
  }

  setTab(tab: SettingsTab): void {
    this.activeTab.set(tab);
  }

  async loadAccounts(): Promise<void> {
    this.accountLoading.set(true);
    this.accountError.set(null);
    try {
      this.accounts.set(await this.accountApi.list());
    } catch (error) {
      this.accountError.set(this.toMessage(error));
      this.accounts.set([]);
    } finally {
      this.accountLoading.set(false);
    }
  }

  async loadInstruments(): Promise<void> {
    this.instrumentLoading.set(true);
    this.instrumentError.set(null);
    try {
      this.instruments.set(await this.instrumentApi.list(false));
    } catch (error) {
      this.instrumentError.set(this.toMessage(error));
      this.instruments.set([]);
    } finally {
      this.instrumentLoading.set(false);
    }
  }

  openNewAccount(): void {
    this.editingAccount.set(null);
    this.accountForm.reset({
      name: '',
      broker: '',
      currency: 'USD',
      initialBalance: 0,
      leverage: 1,
      payoutPercent: 0,
    });
    this.accountForm.controls.initialBalance.enable();
    this.accountFormError.set(null);
    this.instrumentFormOpen.set(false);
    this.accountFormOpen.set(true);
  }

  editAccount(account: AccountDto): void {
    this.editingAccount.set(account);
    this.accountForm.reset({
      name: account.name,
      broker: account.broker,
      currency: account.currency,
      initialBalance: account.initialBalance,
      leverage: account.leverage,
      payoutPercent: account.payoutPercent * 100,
    });
    this.accountForm.controls.initialBalance.disable();
    this.accountFormError.set(null);
    this.instrumentFormOpen.set(false);
    this.accountFormOpen.set(true);
  }

  closeAccountForm(): void {
    if (this.saving()) return;
    this.accountFormOpen.set(false);
    this.editingAccount.set(null);
    this.accountFormError.set(null);
  }

  async submitAccount(): Promise<void> {
    if (this.accountForm.invalid) {
      this.accountForm.markAllAsTouched();
      return;
    }

    this.saving.set(true);
    this.accountFormError.set(null);
    const value = this.accountForm.getRawValue();
    const common = {
      name: value.name.trim(),
      broker: value.broker.trim(),
      currency: value.currency.trim().toUpperCase(),
      leverage: value.leverage,
      payoutPercent: value.payoutPercent / 100,
    };

    try {
      const current = this.editingAccount();
      if (current) {
        await this.accountApi.update(current.id, common);
      } else {
        await this.accountApi.create({ ...common, initialBalance: value.initialBalance });
      }
      this.accountFormOpen.set(false);
      this.editingAccount.set(null);
      await this.loadAccounts();
    } catch (error) {
      this.accountFormError.set(this.toMessage(error));
    } finally {
      this.saving.set(false);
    }
  }

  async toggleAccount(account: AccountDto): Promise<void> {
    if (this.actionId()) return;
    this.actionId.set(account.id);
    this.accountError.set(null);
    try {
      const updated = account.isActive
        ? await this.accountApi.deactivate(account.id)
        : await this.accountApi.reactivate(account.id);
      this.accounts.update(items => items.map(item => item.id === updated.id ? updated : item));
    } catch (error) {
      this.accountError.set(this.toMessage(error));
    } finally {
      this.actionId.set(null);
    }
  }

  openNewInstrument(): void {
    this.editingInstrument.set(null);
    this.instrumentForm.reset({
      symbol: '',
      assetClass: 1,
      contractSize: 1,
      decimalPlaces: 2,
      pipValue: 0,
      payoutPercent: 0,
    });
    this.instrumentFormError.set(null);
    this.accountFormOpen.set(false);
    this.instrumentFormOpen.set(true);
  }

  editInstrument(instrument: InstrumentDto): void {
    this.editingInstrument.set(instrument);
    this.instrumentForm.reset({
      symbol: instrument.symbol,
      assetClass: instrument.assetClass,
      contractSize: instrument.contractSize,
      decimalPlaces: instrument.decimalPlaces,
      pipValue: instrument.pipValue,
      payoutPercent: instrument.payoutPercent * 100,
    });
    this.instrumentFormError.set(null);
    this.accountFormOpen.set(false);
    this.instrumentFormOpen.set(true);
  }

  closeInstrumentForm(): void {
    if (this.saving()) return;
    this.instrumentFormOpen.set(false);
    this.editingInstrument.set(null);
    this.instrumentFormError.set(null);
  }

  async submitInstrument(): Promise<void> {
    if (this.instrumentForm.invalid) {
      this.instrumentForm.markAllAsTouched();
      return;
    }

    this.saving.set(true);
    this.instrumentFormError.set(null);
    const value = this.instrumentForm.getRawValue();
    const request = {
      symbol: value.symbol.trim().toUpperCase(),
      assetClass: value.assetClass,
      contractSize: value.contractSize,
      decimalPlaces: value.decimalPlaces,
      pipValue: value.pipValue,
      payoutPercent: value.payoutPercent / 100,
    };

    try {
      const current = this.editingInstrument();
      if (current) {
        await this.instrumentApi.update(current.id, request);
      } else {
        await this.instrumentApi.create(request);
      }
      this.instrumentFormOpen.set(false);
      this.editingInstrument.set(null);
      await this.loadInstruments();
    } catch (error) {
      this.instrumentFormError.set(this.toMessage(error));
    } finally {
      this.saving.set(false);
    }
  }

  async deactivateInstrument(instrument: InstrumentDto): Promise<void> {
    if (this.actionId()) return;
    this.actionId.set(instrument.id);
    this.instrumentError.set(null);
    try {
      const updated = await this.instrumentApi.deactivate(instrument.id);
      this.instruments.update(items => items.map(item => item.id === updated.id ? updated : item));
    } catch (error) {
      this.instrumentError.set(this.toMessage(error));
    } finally {
      this.actionId.set(null);
    }
  }

  askDelete(kind: DeleteKind, id: string, label: string): void {
    this.deleteTarget.set({ kind, id, label });
    this.deleteError.set(null);
  }

  closeDelete(): void {
    if (this.deleting()) return;
    this.deleteTarget.set(null);
    this.deleteError.set(null);
  }

  async confirmDelete(): Promise<void> {
    const target = this.deleteTarget();
    if (!target || this.deleting()) return;

    this.deleting.set(true);
    this.deleteError.set(null);
    try {
      if (target.kind === 'account') {
        await this.accountApi.delete(target.id);
        this.accounts.update(items => items.filter(item => item.id !== target.id));
      } else {
        await this.instrumentApi.delete(target.id);
        this.instruments.update(items => items.filter(item => item.id !== target.id));
      }
      this.deleteTarget.set(null);
    } catch (error) {
      this.deleteError.set(this.toMessage(error));
    } finally {
      this.deleting.set(false);
    }
  }

  isInvalid(control: { invalid: boolean; touched: boolean }): boolean {
    return control.invalid && control.touched;
  }

  currencyIcon(currency: string): string {
    const symbols: Record<string, string> = { USD: '$', EUR: '€', GBP: '£', JPY: '¥' };
    return symbols[currency.toUpperCase()] ?? currency.slice(0, 1).toUpperCase();
  }

  assetClassLabel(assetClass: AssetClass): string {
    return ASSET_CLASS_LABELS[assetClass];
  }

  assetIcon(assetClass: AssetClass): string {
    const icons: Record<AssetClass, string> = { 1: 'FX', 2: '₿', 3: '01', 4: 'Au', 5: '↗' };
    return icons[assetClass];
  }

  private toMessage(error: unknown): string {
    if (error instanceof HttpErrorResponse) {
      const detail = error.error?.detail;
      if (typeof detail === 'string' && detail) return detail;
      if (error.status === 0) return 'No se pudo conectar con el servidor.';
      return error.message || `Error HTTP ${error.status}.`;
    }
    if (error instanceof Error && error.message) return error.message;
    if (typeof error === 'string') return error;
    return 'Ocurrió un error inesperado.';
  }
}
