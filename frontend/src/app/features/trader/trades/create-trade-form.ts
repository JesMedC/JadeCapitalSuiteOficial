import {
  ChangeDetectionStrategy,
  Component,
  HostListener,
  OnDestroy,
  computed,
  effect,
  inject,
  input,
  output,
  signal,
} from '@angular/core';
import { HttpErrorResponse } from '@angular/common/http';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { toSignal } from '@angular/core/rxjs-interop';
import {
  MARKET_TYPE_LABELS,
  MarketType,
} from '@core/api/account-api.service';
import {
  AssetClass,
  activeAssetClasses as activeAssetClassesFor,
  assetClassLabel as labelForAssetClass,
  hasAssetClass,
} from '@core/api/instrument-api.service';
import {
  OpenTradeRequest,
  TradeApiService,
  TradeDirection,
  TradeDto,
} from '@core/api/trade-api.service';
import { AccountState } from '@core/state/account.state';
import { InstrumentState } from '@core/state/instrument.state';
import {
  PreTradeChecklist,
  PreTradeChecklistPayload,
} from './pre-trade-checklist';
import { PositionSizeCalculator } from './position-size-calculator';
import { PositionSizeCalcResult } from '@core/api/position-size.service';
import { RiskProfileState } from '@core/state/risk-profile.state';

type TradeTab = MarketType;

const SESSION_OPTIONS: ReadonlyArray<{ value: string; label: string }> = [
  { value: 'london', label: 'London' },
  { value: 'new-york', label: 'New York' },
  { value: 'asia', label: 'Asia' },
  { value: 'sydney', label: 'Sydney' },
];

const SLIDE_ANIMATION_MS = 280;

@Component({
  selector: 'jcs-create-trade-form',
  standalone: true,
  imports: [ReactiveFormsModule, RouterLink, PreTradeChecklist, PositionSizeCalculator],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @if (rendered()) {
      <div
        class="panel-shell"
        [class.panel-shell--visible]="visible()"
        role="presentation"
        aria-hidden="false">
        <div class="panel-backdrop" (click)="cancel()" aria-hidden="true"></div>

        <aside
          class="jcs-card jcs-card--glow panel-card"
          role="dialog"
          aria-modal="true"
          aria-labelledby="ct-title">

          <header class="panel-head">
            <div class="panel-head-text">
              <h3 id="ct-title">Nueva operación</h3>
              <p class="jcs-muted">
                Registrá un trade manualmente. La operación queda abierta hasta que la cierres.
              </p>
            </div>
            <button
              type="button"
              class="panel-close"
              (click)="cancel()"
              [disabled]="submitting()"
              aria-label="Cerrar panel">×</button>
          </header>

          <div class="panel-tabs" role="tablist" aria-label="Tipo de operación">
            <button
              type="button"
              role="tab"
              class="panel-tab"
              [class.panel-tab--active]="activeTab() === 1"
              [attr.aria-selected]="activeTab() === 1"
              [disabled]="!hasForexAccounts() && hasBinaryAccounts()"
              (click)="setTab(1)">
              <span class="panel-tab-dot panel-tab-dot--forex" aria-hidden="true"></span>
              Forex
              @if (!hasForexAccounts() && hasBinaryAccounts()) {
                <span class="panel-tab-hint">Sin cuentas Forex</span>
              }
            </button>
            <button
              type="button"
              role="tab"
              class="panel-tab"
              [class.panel-tab--active]="activeTab() === 2"
              [attr.aria-selected]="activeTab() === 2"
              [disabled]="!hasBinaryAccounts() && hasForexAccounts()"
              (click)="setTab(2)">
              <span class="panel-tab-dot panel-tab-dot--binary" aria-hidden="true"></span>
              Binarias
              @if (!hasBinaryAccounts() && hasForexAccounts()) {
                <span class="panel-tab-hint">Sin cuentas Binarias</span>
              }
            </button>
          </div>

          <div class="panel-body">
            @if (loading()) {
              <div class="loading-state" aria-live="polite">
                <span class="loading-dot" aria-hidden="true"></span>
                <span>Cargando cuentas e instrumentos…</span>
              </div>
            } @else if (dataError()) {
              <div class="form-error" role="alert">
                <span>{{ dataError() }}</span>
                <button type="button" class="error-retry" (click)="retryData()">Reintentar</button>
              </div>
            } @else if (hasNoData()) {
              <div class="empty-state">
                <span class="empty-icon" aria-hidden="true">⚠</span>
                <h4>No se puede crear la operación</h4>
                @if (activeAccounts().length === 0) {
                  <p class="jcs-muted">Necesitás al menos una cuenta activa.</p>
                }
                @if (filteredInstruments().length === 0) {
                  <p class="jcs-muted">
                    No hay instrumentos de tipo
                    {{ activeTab() === 1 ? 'Forex' : 'Binarias' }} activos.
                  </p>
                }
                <a class="jcs-btn jcs-btn--primary" routerLink="/app/settings">Ir a Configuración</a>
              </div>
            } @else {
              <form [formGroup]="form" (ngSubmit)="submit()" class="panel-form" novalidate>
                <!-- ============== Common fields ============== -->
                <div class="form-grid">
                  <div class="field">
                    <label class="jcs-label" for="ct-account">Cuenta</label>
                    <select
                      id="ct-account"
                      class="jcs-input"
                      formControlName="accountId"
                      [disabled]="submitting()"
                      [class.jcs-input--error]="isInvalid(form.controls.accountId)">
                      <option value="">Seleccioná una cuenta</option>
                      @for (a of accountsForTab(); track a.id) {
                        <option [value]="a.id">
                          {{ a.name }} · {{ a.broker }} · {{ marketTypeLabel(a.marketType) }}
                        </option>
                      }
                    </select>
                    @if (isInvalid(form.controls.accountId)) {
                      <span class="field-error">Seleccioná una cuenta.</span>
                    }
                  </div>

                  <div class="field">
                    <label class="jcs-label" for="ct-instrument">Instrumento</label>
                    <select
                      id="ct-instrument"
                      class="jcs-input"
                      formControlName="instrumentId"
                      [disabled]="submitting()"
                      [class.jcs-input--error]="isInvalid(form.controls.instrumentId)">
                      <option value="">Seleccioná un instrumento</option>
                      @for (i of filteredInstruments(); track i.id) {
                        <option [value]="i.id">{{ i.symbol }} · {{ assetClassLabelForInstrument(i.assetClasses) }}</option>
                      }
                    </select>
                    @if (isInvalid(form.controls.instrumentId)) {
                      <span class="field-error">Seleccioná un instrumento.</span>
                    } @else {
                      <span class="field-hint">
                        @if (filteredInstruments().length === 0) {
                          No hay instrumentos compatibles con este tipo de operación.
                        } @else {
                          {{ filteredInstruments().length }} disponibles.
                        }
                      </span>
                    }
                  </div>
                </div>

                <!-- ============== Forex tab ============== -->
                @if (activeTab() === 1) {
                  <fieldset class="tab-section">
                    <legend class="tab-section-legend">Detalles Forex</legend>

                    <div class="field">
                      <label class="jcs-label">Dirección</label>
                      <div class="dir-toggle" role="group" aria-label="Dirección">
                        <button
                          type="button"
                          class="dir-pill"
                          [class.dir-pill--active]="form.controls.direction.value === 1"
                          [disabled]="submitting()"
                          (click)="setDirection(1)"
                          aria-label="Compra / Long">
                          <span class="dot dot--long" aria-hidden="true"></span>
                          COMPRA
                        </button>
                        <button
                          type="button"
                          class="dir-pill"
                          [class.dir-pill--active]="form.controls.direction.value === 2"
                          [disabled]="submitting()"
                          (click)="setDirection(2)"
                          aria-label="Venta / Short">
                          <span class="dot dot--short" aria-hidden="true"></span>
                          VENTA
                        </button>
                      </div>
                    </div>

                    <div class="form-grid">
                      <div class="field">
                        <label class="jcs-label" for="ct-entry">Entrada</label>
                        <div class="input-suffix">
                          <input
                            id="ct-entry"
                            class="jcs-input jcs-num"
                            type="number"
                            min="0.00000001"
                            [step]="priceStep()"
                            formControlName="entryPrice"
                            [disabled]="submitting()"
                            placeholder="0.00"
                            [class.jcs-input--error]="isInvalid(form.controls.entryPrice)">
                          <span class="suffix" [class.suffix--empty]="!entryPriceCurrency()">
                            {{ entryPriceCurrency() || '—' }}
                          </span>
                        </div>
                        @if (isInvalid(form.controls.entryPrice)) {
                          <span class="field-error">Ingresá un precio mayor a 0.</span>
                        } @else {
                          <span class="field-hint">
                            Decimales sugeridos: {{ selectedInstrument()?.decimalPlaces ?? '—' }}
                          </span>
                        }
                      </div>

                      <div class="field">
                        <label class="jcs-label" for="ct-exit">Salida (opcional)</label>
                        <div class="input-suffix">
                          <input
                            id="ct-exit"
                            class="jcs-input jcs-num"
                            type="number"
                            min="0.00000001"
                            [step]="priceStep()"
                            formControlName="exitPrice"
                            [disabled]="submitting()"
                            placeholder="0.00">
                          <span class="suffix" [class.suffix--empty]="!entryPriceCurrency()">
                            {{ entryPriceCurrency() || '—' }}
                          </span>
                        </div>
                        <span class="field-hint">Para trades ya cerrados.</span>
                      </div>

                      <div class="field">
                        <label class="jcs-label" for="ct-sl">Stop Loss (opcional)</label>
                        <div class="input-suffix">
                          <input
                            id="ct-sl"
                            class="jcs-input jcs-num"
                            type="number"
                            min="0.00000001"
                            [step]="priceStep()"
                            formControlName="stopLoss"
                            [disabled]="submitting()"
                            placeholder="0.00">
                          <span class="suffix" [class.suffix--empty]="!entryPriceCurrency()">
                            {{ entryPriceCurrency() || '—' }}
                          </span>
                        </div>
                      </div>

                      <div class="field">
                        <label class="jcs-label" for="ct-tp">Take Profit (opcional)</label>
                        <div class="input-suffix">
                          <input
                            id="ct-tp"
                            class="jcs-input jcs-num"
                            type="number"
                            min="0.00000001"
                            [step]="priceStep()"
                            formControlName="takeProfit"
                            [disabled]="submitting()"
                            placeholder="0.00">
                          <span class="suffix" [class.suffix--empty]="!entryPriceCurrency()">
                            {{ entryPriceCurrency() || '—' }}
                          </span>
                        </div>
                      </div>
                    </div>

                    <div class="form-grid">
                      <div class="field">
                        <label class="jcs-label" for="ct-risk">Riesgo (R %)</label>
                        <div class="input-suffix">
                          <input
                            id="ct-risk"
                            class="jcs-input jcs-num"
                            type="number"
                            min="0"
                            step="0.01"
                            formControlName="riskPercent"
                            [disabled]="submitting()"
                            placeholder="1.0">
                          <span class="suffix suffix--empty">%</span>
                        </div>
                        <span class="field-hint">Porcentaje del balance que se arriesga.</span>
                      </div>

                      <div class="field">
                        <label class="jcs-label" for="ct-rmult">R Múltiple</label>
                        <div class="input-suffix">
                          <input
                            id="ct-rmult"
                            class="jcs-input jcs-num readonly"
                            type="text"
                            [value]="rMultipleLabel()"
                            readonly
                            tabindex="-1">
                          <span class="suffix suffix--empty">R</span>
                        </div>
                        <span class="field-hint">{{ rMultipleHint() }}</span>
                      </div>
                    </div>

                    <div class="form-grid">
                      <div class="field">
                        <label class="jcs-label" for="ct-volume">Tamaño de posición</label>
                        <div class="input-suffix">
                          <input
                            id="ct-volume"
                            class="jcs-input jcs-num"
                            type="number"
                            min="0.00000001"
                            [step]="volumeStep()"
                            formControlName="volume"
                            [disabled]="submitting()"
                            placeholder="0.00"
                            [class.jcs-input--error]="isInvalid(form.controls.volume)">
                          <span class="suffix" [class.suffix--empty]="!volumeCurrency()">
                            {{ volumeCurrency() || 'lotes' }}
                          </span>
                        </div>
                        @if (isInvalid(form.controls.volume)) {
                          <span class="field-error">Ingresá un volumen mayor a 0.</span>
                        } @else {
                          <span class="field-hint">{{ positionSizeHint() }}</span>
                        }
                      </div>

                      <div class="field">
                        <label class="jcs-label" for="ct-pnl">P&amp;L esperado</label>
                        <div class="input-suffix">
                          <input
                            id="ct-pnl"
                            class="jcs-input jcs-num readonly"
                            type="text"
                            [value]="expectedPnLLabel()"
                            readonly
                            tabindex="-1">
                          <span
                            class="suffix"
                            [class.suffix--empty]="!volumeCurrency()">
                            {{ volumeCurrency() || '—' }}
                          </span>
                        </div>
                        <span class="field-hint">{{ expectedPnLHint() }}</span>
                      </div>
                    </div>

                    <div class="form-grid">
                      <div class="field">
                        <label class="jcs-label" for="ct-opened">Fecha apertura</label>
                        <input
                          id="ct-opened"
                          class="jcs-input"
                          type="datetime-local"
                          formControlName="openedAt"
                          [disabled]="submitting()">
                      </div>

                      <div class="field">
                        <label class="jcs-label" for="ct-closed">Fecha cierre (opcional)</label>
                        <input
                          id="ct-closed"
                          class="jcs-input"
                          type="datetime-local"
                          formControlName="closedAt"
                          [disabled]="submitting()">
                      </div>
                    </div>
                  </fieldset>
                }

                <!-- ============== Binary tab ============== -->
                @if (activeTab() === 2) {
                  <fieldset class="tab-section">
                    <legend class="tab-section-legend">Detalles Binarias</legend>

                    <div class="field">
                      <label class="jcs-label">Dirección</label>
                      <div class="dir-toggle" role="group" aria-label="Dirección">
                        <button
                          type="button"
                          class="dir-pill"
                          [class.dir-pill--active]="form.controls.direction.value === 1"
                          [disabled]="submitting()"
                          (click)="setDirection(1)"
                          aria-label="Call">
                          <span class="dot dot--long" aria-hidden="true"></span>
                          CALL
                        </button>
                        <button
                          type="button"
                          class="dir-pill"
                          [class.dir-pill--active]="form.controls.direction.value === 2"
                          [disabled]="submitting()"
                          (click)="setDirection(2)"
                          aria-label="Put">
                          <span class="dot dot--short" aria-hidden="true"></span>
                          PUT
                        </button>
                      </div>
                    </div>

                    <div class="form-grid">
                      <div class="field">
                        <label class="jcs-label" for="ct-entry-b">Entrada (strike)</label>
                        <div class="input-suffix">
                          <input
                            id="ct-entry-b"
                            class="jcs-input jcs-num"
                            type="number"
                            min="0.00000001"
                            [step]="priceStep()"
                            formControlName="entryPrice"
                            [disabled]="submitting()"
                            placeholder="0.00"
                            [class.jcs-input--error]="isInvalid(form.controls.entryPrice)">
                          <span class="suffix" [class.suffix--empty]="!entryPriceCurrency()">
                            {{ entryPriceCurrency() || '—' }}
                          </span>
                        </div>
                        @if (isInvalid(form.controls.entryPrice)) {
                          <span class="field-error">Ingresá un strike mayor a 0.</span>
                        } @else {
                          <span class="field-hint">
                            Decimales: {{ selectedInstrument()?.decimalPlaces ?? '—' }}
                          </span>
                        }
                      </div>

                      <div class="field">
                        <label class="jcs-label" for="ct-payout">Payout (%)</label>
                        <div class="input-suffix">
                          <input
                            id="ct-payout"
                            class="jcs-input jcs-num readonly"
                            type="text"
                            [value]="payoutLabel()"
                            readonly
                            tabindex="-1">
                          <span class="suffix suffix--empty">%</span>
                        </div>
                        <span class="field-hint">{{ payoutHint() }}</span>
                      </div>

                      <div class="field">
                        <label class="jcs-label" for="ct-amount">Importe</label>
                        <div class="input-suffix">
                          <input
                            id="ct-amount"
                            class="jcs-input jcs-num"
                            type="number"
                            min="0.01"
                            step="0.01"
                            formControlName="amount"
                            [disabled]="submitting()"
                            placeholder="0.00"
                            [class.jcs-input--error]="isInvalid(form.controls.amount)">
                          <span class="suffix" [class.suffix--empty]="!volumeCurrency()">
                            {{ volumeCurrency() || '—' }}
                          </span>
                        </div>
                        @if (isInvalid(form.controls.amount)) {
                          <span class="field-error">Ingresá un importe mayor a 0.</span>
                        } @else {
                          <span class="field-hint">Cuánto apostás a esta operación.</span>
                        }
                      </div>

                      <div class="field">
                        <label class="jcs-label" for="ct-pnl-b">P&amp;L potencial</label>
                        <div class="input-suffix">
                          <input
                            id="ct-pnl-b"
                            class="jcs-input jcs-num readonly"
                            type="text"
                            [value]="potentialPnLLabel()"
                            readonly
                            tabindex="-1">
                          <span
                            class="suffix"
                            [class.suffix--empty]="!volumeCurrency()">
                            {{ volumeCurrency() || '—' }}
                          </span>
                        </div>
                        <span class="field-hint">{{ potentialPnLHint() }}</span>
                      </div>
                    </div>

                    <div class="form-grid">
                      <div class="field">
                        <label class="jcs-label" for="ct-opened-b">Fecha apertura</label>
                        <input
                          id="ct-opened-b"
                          class="jcs-input"
                          type="datetime-local"
                          formControlName="openedAt"
                          [disabled]="submitting()">
                      </div>

                      <div class="field">
                        <label class="jcs-label" for="ct-expires">Fecha expiración (opcional)</label>
                        <input
                          id="ct-expires"
                          class="jcs-input"
                          type="datetime-local"
                          formControlName="expiresAt"
                          [disabled]="submitting()">
                      </div>
                    </div>
                  </fieldset>
                }

                <!-- ============== Shared optional fields ============== -->
                <fieldset class="tab-section">
                  <legend class="tab-section-legend">Contexto</legend>

                  <div class="form-grid">
                    <div class="field">
                      <label class="jcs-label" for="ct-strategy">Estrategia (opcional)</label>
                      <input
                        id="ct-strategy"
                        class="jcs-input"
                        type="text"
                        formControlName="strategy"
                        [disabled]="submitting()"
                        maxlength="80"
                        placeholder="Ej.: Breakout Londres, Order block H4…">
                      <span class="field-hint">{{ form.controls.strategy.value.length }} / 80</span>
                    </div>

                    <div class="field">
                      <label class="jcs-label" for="ct-session">Sesión (opcional)</label>
                      <select
                        id="ct-session"
                        class="jcs-input"
                        formControlName="session"
                        [disabled]="submitting()">
                        <option value="">Sin definir</option>
                        @for (s of sessionOptions; track s.value) {
                          <option [value]="s.value">{{ s.label }}</option>
                        }
                      </select>
                    </div>
                  </div>

                  <div class="field field--full">
                    <label class="jcs-label" for="ct-notes">Notas (opcional)</label>
                    <textarea
                      id="ct-notes"
                      class="jcs-input notes-input"
                      formControlName="notes"
                      [disabled]="submitting()"
                      maxlength="2000"
                      rows="3"
                      placeholder="Contexto, setup, por qué tomaste el trade…"></textarea>
                    <span class="field-hint">{{ form.controls.notes.value.length }} / 2000</span>
                  </div>

                  <div class="field field--full">
                    <label class="jcs-label">Captura de pantalla (opcional)</label>
                    <div
                      class="screenshot-drop"
                      [class.screenshot-drop--hover]="screenshotDrag()"
                      (dragover)="onScreenshotDragOver($event)"
                      (dragleave)="onScreenshotDragLeave($event)"
                      (drop)="onScreenshotDrop($event)"
                      (click)="screenshotInput.click()"
                      role="button"
                      tabindex="0">
                      @if (screenshotPreviewUrl()) {
                        <img
                          class="screenshot-preview"
                          [src]="screenshotPreviewUrl()"
                          alt="Vista previa de la captura">
                        <div class="screenshot-meta">
                          <span class="screenshot-name jcs-num">{{ screenshotName() }}</span>
                          <button
                            type="button"
                            class="screenshot-clear"
                            (click)="clearScreenshot($event)"
                            [disabled]="submitting()"
                            aria-label="Quitar captura">Quitar</button>
                        </div>
                      } @else {
                        <span class="screenshot-icon" aria-hidden="true">📷</span>
                        <span class="screenshot-cta">
                          Arrastrá una imagen o <strong>hacé click</strong> para seleccionar
                        </span>
                        <span class="screenshot-hint">Solo visual — no se guarda con el trade.</span>
                      }
                      <input
                        #screenshotInput
                        class="screenshot-input"
                        type="file"
                        accept="image/*"
                        [disabled]="submitting()"
                        (change)="onScreenshotPicked($event)">
                    </div>
                  </div>
                </fieldset>

                @if (submitError()) {
                  <div class="form-error" role="alert">{{ submitError() }}</div>
                }
              </form>
            }
          </div>

          <footer class="panel-foot">
            <!-- ============== Position-size calculator (slice 1b) ============== -->
            <jcs-position-size-calculator
              class="panel-calculator"
              [profile]="riskProfile.profile()"
              [defaultStopLossDistance]="defaultStopLossForCalc()"
              (calculated)="onPositionSizeCalculated($event)" />

            <!-- ============== Pre-trade checklist (slice 1c.2) ============== -->
            <jcs-pre-trade-checklist
              class="panel-checklist"
              [errorCode]="checklistErrorCode()"
              [fieldErrors]="checklistFieldErrors()"
              (submission)="onChecklistSubmission($event)"
              (cancelled)="onChecklistCancelled()" />

            <div class="panel-actions">
              <button
                type="button"
                class="jcs-btn jcs-btn--ghost"
                (click)="cancel()"
                [disabled]="submitting()">Cancelar</button>
              <button
                type="button"
                class="jcs-btn jcs-btn--primary"
                (click)="submit()"
                [disabled]="submitting() || !canSubmit()">
                @if (submitting()) {
                  <span class="btn-spinner" aria-hidden="true"></span>
                  Guardando…
                } @else {
                  Crear operación
                }
              </button>
            </div>
          </footer>
        </aside>
      </div>
    }
  `,
  styles: [`
    :host { display: contents; }

    /* ============== Slide-out shell ============== */
    .panel-shell {
      position: fixed;
      inset: 0;
      z-index: 200;
      pointer-events: none;
    }
    .panel-shell--visible {
      pointer-events: auto;
    }

    .panel-backdrop {
      position: absolute;
      inset: 0;
      background: rgba(0, 0, 0, 0.55);
      backdrop-filter: blur(2px);
      opacity: 0;
      transition: opacity 280ms cubic-bezier(0.16, 1, 0.3, 1);
    }
    .panel-shell--visible .panel-backdrop { opacity: 1; }

    .panel-card {
      position: absolute;
      top: 0;
      right: 0;
      bottom: 0;
      width: min(460px, 100vw);
      display: flex;
      flex-direction: column;
      gap: 0;
      padding: 0;
      border-radius: 0;
      border-left: 1px solid var(--border-active);
      box-shadow: -16px 0 48px rgba(0, 0, 0, 0.5);
      transform: translateX(100%);
      transition: transform 280ms cubic-bezier(0.16, 1, 0.3, 1);
      overflow: hidden;
    }
    .panel-shell--visible .panel-card { transform: translateX(0); }

    /* ============== Header ============== */
    .panel-head {
      display: flex;
      justify-content: space-between;
      align-items: flex-start;
      gap: var(--sp-3);
      padding: var(--sp-5) var(--sp-6) var(--sp-3);
      flex-shrink: 0;
    }
    .panel-head-text { min-width: 0; }
    .panel-head h3 { margin: 0 0 var(--sp-1); font-size: var(--fs-xl); }
    .panel-head p { margin: 0; font-size: var(--fs-sm); max-width: 360px; }
    .panel-close {
      width: 34px;
      height: 34px;
      flex-shrink: 0;
      border: 1px solid var(--border);
      border-radius: var(--radius-sm);
      background: transparent;
      color: var(--text-muted);
      font-size: var(--fs-xl);
      line-height: 1;
      cursor: pointer;
      transition: border-color 150ms, color 150ms;
    }
    .panel-close:hover:not(:disabled) {
      border-color: var(--border-active);
      color: var(--text-main);
    }
    .panel-close:disabled { opacity: 0.5; cursor: not-allowed; }

    /* ============== Tabs ============== */
    .panel-tabs {
      display: flex;
      gap: var(--sp-2);
      padding: 0 var(--sp-6) var(--sp-3);
      border-bottom: 1px solid var(--border-soft);
      flex-shrink: 0;
    }
    .panel-tab {
      display: inline-flex;
      align-items: center;
      gap: var(--sp-2);
      padding: var(--sp-2) var(--sp-4);
      background: transparent;
      border: 1px solid var(--border-soft);
      border-radius: var(--radius-pill, 9999px);
      color: var(--text-secondary);
      font: inherit;
      font-size: var(--fs-sm);
      font-weight: 600;
      cursor: pointer;
      transition: all 150ms;
    }
    .panel-tab:hover:not(:disabled) { color: var(--text-main); }
    .panel-tab:disabled {
      opacity: 0.4;
      cursor: not-allowed;
    }
    .panel-tab--active {
      background: var(--green-soft);
      border-color: var(--border-active);
      color: var(--green);
    }
    .panel-tab-dot {
      width: 8px;
      height: 8px;
      border-radius: 50%;
    }
    .panel-tab-dot--forex { background: var(--green); }
    .panel-tab-dot--binary { background: var(--yellow); }
    .panel-tab-hint {
      font-size: 0.65rem;
      color: var(--text-muted);
      font-weight: 500;
      letter-spacing: 0.04em;
    }

    /* ============== Body ============== */
    .panel-body {
      flex: 1;
      min-height: 0;
      overflow-y: auto;
      padding: var(--sp-5) var(--sp-6);
    }
    .panel-form {
      display: flex;
      flex-direction: column;
      gap: var(--sp-5);
    }

    /* ============== Footer ============== */
    .panel-foot {
      display: flex;
      flex-direction: column;
      gap: var(--sp-4);
      padding: var(--sp-4) var(--sp-6);
      border-top: 1px solid var(--border-soft);
      background: var(--bg-card);
      flex-shrink: 0;
      max-height: 60vh;
      overflow-y: auto;
    }
    .panel-checklist { display: block; }
    .panel-calculator { display: block; }
    .panel-actions {
      display: flex;
      justify-content: flex-end;
      gap: var(--sp-3);
      flex-wrap: wrap;
    }

    /* ============== Sections & fields ============== */
    .tab-section {
      border: 1px solid var(--border-soft);
      border-radius: var(--radius-md);
      padding: var(--sp-4);
      display: flex;
      flex-direction: column;
      gap: var(--sp-4);
      margin: 0;
    }
    .tab-section-legend {
      padding: 0 var(--sp-2);
      font-size: var(--fs-xs);
      font-weight: 600;
      color: var(--text-muted);
      text-transform: uppercase;
      letter-spacing: 0.08em;
    }

    .form-grid {
      display: grid;
      grid-template-columns: repeat(2, minmax(0, 1fr));
      gap: var(--sp-4);
    }
    .field {
      display: flex;
      flex-direction: column;
      gap: var(--sp-2);
      min-width: 0;
    }
    .field--full { grid-column: 1 / -1; }
    .field-error { color: var(--red); font-size: var(--fs-xs); }
    .field-hint {
      color: var(--text-muted);
      font-size: var(--fs-xs);
      font-family: var(--font-mono);
    }

    .input-suffix {
      position: relative;
      display: flex;
      align-items: stretch;
    }
    .input-suffix .jcs-input {
      flex: 1;
      min-width: 0;
      padding-right: 60px;
    }
    .input-suffix .suffix {
      position: absolute;
      top: 50%;
      right: var(--sp-3);
      transform: translateY(-50%);
      font-family: var(--font-mono);
      font-size: var(--fs-xs);
      font-weight: 600;
      color: var(--green);
      pointer-events: none;
      padding: 2px var(--sp-2);
      background: var(--bg-card-soft);
      border-radius: var(--radius-xs);
      border: 1px solid var(--border-soft);
    }
    .input-suffix .suffix--empty {
      color: var(--text-muted);
      background: transparent;
      border-color: transparent;
    }
    .input-suffix input.readonly {
      background: var(--bg-card-soft);
      color: var(--text-secondary);
      cursor: not-allowed;
    }

    .notes-input {
      font-family: inherit;
      resize: vertical;
      min-height: 80px;
      line-height: 1.5;
    }

    /* ============== Direction toggle ============== */
    .dir-toggle {
      display: inline-flex;
      gap: var(--sp-1);
      background: var(--bg-card-soft);
      border: 1px solid var(--border-soft);
      border-radius: var(--radius-sm);
      padding: var(--sp-1);
      width: fit-content;
    }
    .dir-pill {
      display: inline-flex;
      align-items: center;
      gap: var(--sp-2);
      padding: var(--sp-2) var(--sp-4);
      background: transparent;
      border: 1px solid transparent;
      border-radius: var(--radius-sm);
      color: var(--text-secondary);
      font: inherit;
      font-size: var(--fs-sm);
      font-weight: 600;
      cursor: pointer;
      transition: background 150ms, color 150ms, border-color 150ms;
    }
    .dir-pill:hover:not(:disabled) { color: var(--text-main); }
    .dir-pill:disabled { opacity: 0.45; cursor: not-allowed; }
    .dir-pill--active {
      background: var(--bg-elevated);
      color: var(--text-main);
      border-color: var(--border-active);
    }
    .dir-toggle .dot {
      width: 6px;
      height: 6px;
      border-radius: 50%;
    }
    .dir-toggle .dot--long { background: var(--green); }
    .dir-toggle .dot--short { background: var(--red); }

    /* ============== Screenshot drop zone ============== */
    .screenshot-drop {
      position: relative;
      display: flex;
      flex-direction: column;
      align-items: center;
      justify-content: center;
      gap: var(--sp-2);
      min-height: 120px;
      padding: var(--sp-5);
      border: 1px dashed var(--border);
      border-radius: var(--radius-md);
      background: var(--bg-card-soft);
      color: var(--text-muted);
      cursor: pointer;
      transition: border-color 150ms, background 150ms;
    }
    .screenshot-drop:hover,
    .screenshot-drop:focus-visible,
    .screenshot-drop--hover {
      border-color: var(--border-active);
      background: var(--green-soft);
      outline: none;
    }
    .screenshot-input {
      position: absolute;
      width: 1px;
      height: 1px;
      opacity: 0;
      pointer-events: none;
    }
    .screenshot-icon {
      font-size: var(--fs-xl);
    }
    .screenshot-cta {
      font-size: var(--fs-sm);
      color: var(--text-secondary);
    }
    .screenshot-cta strong { color: var(--green); }
    .screenshot-hint {
      font-size: var(--fs-xs);
      color: var(--text-muted);
      font-family: var(--font-mono);
    }
    .screenshot-preview {
      max-width: 100%;
      max-height: 180px;
      border-radius: var(--radius-sm);
      border: 1px solid var(--border-soft);
    }
    .screenshot-meta {
      display: flex;
      align-items: center;
      gap: var(--sp-3);
    }
    .screenshot-name {
      font-size: var(--fs-xs);
      color: var(--text-secondary);
      max-width: 200px;
      overflow: hidden;
      text-overflow: ellipsis;
      white-space: nowrap;
    }
    .screenshot-clear {
      padding: 2px var(--sp-2);
      background: transparent;
      border: 1px solid var(--border);
      border-radius: var(--radius-xs);
      color: var(--text-muted);
      font: inherit;
      font-size: var(--fs-xs);
      font-weight: 600;
      cursor: pointer;
    }
    .screenshot-clear:hover:not(:disabled) {
      border-color: var(--red);
      color: var(--red);
    }

    /* ============== States ============== */
    .loading-state {
      display: flex;
      align-items: center;
      gap: var(--sp-3);
      padding: var(--sp-5);
      color: var(--text-muted);
      font-size: var(--fs-sm);
    }
    .loading-dot {
      width: 10px;
      height: 10px;
      border-radius: 50%;
      background: var(--green);
      animation: pulse 1.2s ease-in-out infinite;
    }
    @keyframes pulse {
      0%, 100% { opacity: 0.4; transform: scale(0.85); }
      50%      { opacity: 1;   transform: scale(1); }
    }

    .empty-state {
      display: flex;
      flex-direction: column;
      align-items: center;
      justify-content: center;
      gap: var(--sp-3);
      padding: var(--sp-8) var(--sp-4);
      text-align: center;
    }
    .empty-state h4 { margin: 0; font-size: var(--fs-lg); }
    .empty-state p { margin: 0; font-size: var(--fs-sm); max-width: 360px; }
    .empty-icon {
      display: inline-flex;
      align-items: center;
      justify-content: center;
      width: 48px;
      height: 48px;
      border: 1px solid rgba(255, 64, 87, 0.4);
      background: rgba(255, 64, 87, 0.1);
      color: var(--red);
      border-radius: 50%;
      font-size: var(--fs-xl);
      font-weight: 700;
    }

    .form-error {
      display: flex;
      align-items: center;
      justify-content: space-between;
      gap: var(--sp-3);
      flex-wrap: wrap;
      flex-shrink: 0;
      padding: var(--sp-3) var(--sp-4);
      background: rgba(255, 64, 87, 0.08);
      border: 1px solid rgba(255, 64, 87, 0.35);
      border-radius: var(--radius-sm);
      color: var(--red);
      font-size: var(--fs-sm);
    }
    .form-error > span,
    .form-error > .form-error-text {
      flex: 1 1 200px;
      min-width: 0;
      word-break: break-word;
      overflow-wrap: anywhere;
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
      flex-shrink: 0;
      align-self: flex-end;
    }
    .error-retry:hover { background: rgba(255, 64, 87, 0.15); }

    .btn-spinner {
      display: inline-block;
      width: 14px;
      height: 14px;
      border: 2px solid rgba(5, 11, 16, 0.3);
      border-top-color: #050B10;
      border-radius: 50%;
      animation: spin 0.8s linear infinite;
    }
    @keyframes spin {
      from { transform: rotate(0); }
      to   { transform: rotate(360deg); }
    }

    @media (max-width: 540px) {
      .form-grid { grid-template-columns: 1fr; }
      .panel-tabs { flex-direction: column; }
      .panel-tab { width: 100%; justify-content: flex-start; }
      .panel-foot { flex-direction: column; }
      .panel-actions { flex-direction: column-reverse; }
      .panel-actions .jcs-btn { width: 100%; }
    }
  `],
})
export class CreateTradeForm implements OnDestroy {
  private readonly fb = inject(FormBuilder);
  private readonly tradeApi = inject(TradeApiService);
  readonly accountState = inject(AccountState);
  readonly instrumentState = inject(InstrumentState);
  /** Slice 1b: active risk profile — fed into the position-size calculator. */
  readonly riskProfile = inject(RiskProfileState);

  readonly visible = input.required<boolean>();
  readonly saved = output<TradeDto>();
  readonly cancelled = output<void>();

  readonly submitting = signal(false);
  readonly submitError = signal<string | null>(null);

  // ===== Pre-trade checklist state (slice 1c.2) =====
  /** Latest submission emitted by `<jcs-pre-trade-checklist>`. Null until the user accepts. */
  readonly checklistSubmission = signal<PreTradeChecklistPayload | null>(null);
  /** Error code from the last 422 RFC 7807 problem (e.g. "pre_trade_checklist.rr_below_target"). */
  readonly checklistErrorCode = signal<string | null>(null);
  /** Field names flagged by the last 422 RFC 7807 problem. */
  readonly checklistFieldErrors = signal<string[]>([]);

  /** Active tab (1 = Forex, 2 = Binary). */
  readonly activeTab = signal<TradeTab>(1);
  /** Rendered during open + close animation. */
  readonly rendered = signal(false);
  /** Screenshot drag hover. */
  readonly screenshotDrag = signal(false);
  /** Screenshot file name for display. */
  readonly screenshotName = signal<string | null>(null);
  /** Object URL for screenshot preview. */
  readonly screenshotPreviewUrl = signal<string | null>(null);

  private screenshotFile: File | null = null;
  private destroyed = false;
  private hideTimer: ReturnType<typeof setTimeout> | null = null;

  readonly sessionOptions = SESSION_OPTIONS;

  readonly form = this.fb.nonNullable.group({
    accountId: ['', Validators.required],
    instrumentId: ['', Validators.required],
    direction: [1 as TradeDirection, Validators.required],
    // Forex "lots". Sin validador de min — se valida en canSubmit según tab.
    volume: [0],
    // Binary "importe" (se mapea a volume en submit cuando tab=binary).
    amount: [0],
    entryPrice: [0, [Validators.required, Validators.min(0.00000001)]],
    // Optional visual-only fields (not persisted to backend).
    exitPrice: [null as number | null],
    stopLoss: [null as number | null],
    takeProfit: [null as number | null],
    riskPercent: [null as number | null],
    strategy: ['', Validators.maxLength(80)],
    notes: ['', Validators.maxLength(2000)],
    session: [''],
    openedAt: [this.nowLocalIso()],
    closedAt: [null as string | null],
    expiresAt: [null as string | null],
  });

  // ====== Signals reactivos a form changes ======
  private readonly accountIdValue = toSignal(
    this.form.controls.accountId.valueChanges,
    { initialValue: this.form.controls.accountId.value },
  );
  private readonly instrumentIdValue = toSignal(
    this.form.controls.instrumentId.valueChanges,
    { initialValue: this.form.controls.instrumentId.value },
  );
  private readonly directionValue = toSignal(
    this.form.controls.direction.valueChanges,
    { initialValue: this.form.controls.direction.value },
  );
  private readonly entryPriceValue = toSignal(
    this.form.controls.entryPrice.valueChanges,
    { initialValue: this.form.controls.entryPrice.value },
  );
  private readonly stopLossValue = toSignal(
    this.form.controls.stopLoss.valueChanges,
    { initialValue: this.form.controls.stopLoss.value },
  );
  private readonly takeProfitValue = toSignal(
    this.form.controls.takeProfit.valueChanges,
    { initialValue: this.form.controls.takeProfit.value },
  );
  private readonly riskPercentValue = toSignal(
    this.form.controls.riskPercent.valueChanges,
    { initialValue: this.form.controls.riskPercent.value },
  );
  private readonly volumeValue = toSignal(
    this.form.controls.volume.valueChanges,
    { initialValue: this.form.controls.volume.value },
  );
  private readonly amountValue = toSignal(
    this.form.controls.amount.valueChanges,
    { initialValue: this.form.controls.amount.value },
  );

  // ====== Derived data ======
  readonly loading = computed(() =>
    this.accountState.loading() || this.instrumentState.loading(),
  );
  readonly dataError = computed(() =>
    this.accountState.error() ?? this.instrumentState.error(),
  );

  readonly activeAccounts = this.accountState.activeAccounts;
  readonly activeInstruments = this.instrumentState.activeInstruments;

  readonly forexAccounts = computed(() => this.activeAccounts().filter(a => a.marketType === 1));
  readonly binaryAccounts = computed(() => this.activeAccounts().filter(a => a.marketType === 2));

  readonly hasForexAccounts = computed(() => this.forexAccounts().length > 0);
  readonly hasBinaryAccounts = computed(() => this.binaryAccounts().length > 0);

  readonly accountsForTab = computed(() => {
    const tab = this.activeTab();
    return tab === 1 ? this.forexAccounts() : this.binaryAccounts();
  });

  /** Bitmask del AssetClass esperado por el tab activo. */
  private readonly activeTabBit = computed<AssetClass>(() =>
    this.activeTab() === 1 ? 1 : 4,
  );

  readonly forexInstruments = computed(() =>
    this.activeInstruments().filter(i => hasAssetClass(i.assetClasses, 1)),
  );
  readonly binaryInstruments = computed(() =>
    this.activeInstruments().filter(i => hasAssetClass(i.assetClasses, 4)),
  );

  /**
   * Filter by the selected account's market type when present (most common path),
   * otherwise fall back to the active tab. Delegates to the InstrumentState so the
   * bitmask logic (Forex=1, Binary=4) lives in a single place.
   */
  readonly filteredInstruments = computed(() => {
    const account = this.selectedAccount();
    return this.instrumentState.forMarketType(account?.marketType ?? this.activeTab());
  });

  readonly selectedAccount = computed(() => {
    const id = this.accountIdValue();
    if (!id) return null;
    return this.accountState.accounts().find(a => a.id === id) ?? null;
  });

  readonly selectedInstrument = computed(() => {
    const id = this.instrumentIdValue();
    if (!id) return null;
    return this.instrumentState.instruments().find(i => i.id === id) ?? null;
  });

  readonly volumeCurrency = computed(() => this.selectedAccount()?.currency ?? '');

  readonly entryPriceCurrency = computed(() => {
    const sym = this.selectedInstrument()?.symbol;
    if (sym && sym.includes('/')) return sym.split('/')[1] ?? 'USD';
    // Binary or generic fallback: use account currency, then USD.
    return this.selectedAccount()?.currency ?? 'USD';
  });

  private readonly decimalsForStep = computed(
    () => this.selectedInstrument()?.decimalPlaces ?? 5,
  );

  readonly volumeStep = computed(() => this.stepFor(this.decimalsForStep()));
  readonly priceStep = computed(() => this.stepFor(this.decimalsForStep()));

  readonly hasNoData = computed(() => {
    if (this.loading() || this.dataError()) return false;
    if (this.activeAccounts().length === 0) return true;
    if (this.filteredInstruments().length === 0) return true;
    return false;
  });

  readonly canSubmit = computed(() => {
    if (this.loading() || this.dataError() || this.hasNoData()) return false;
    if (this.form.invalid) return false;
    const tab = this.activeTab();
    if (tab === 1) {
      return this.volumeValue() >= 0.00000001;
    }
    return this.amountValue() >= 0.01;
  });

  // ====== Auto-calculations (Forex) ======
  readonly rMultipleLabel = computed(() => {
    const entry = this.entryPriceValue();
    const sl = this.stopLossValue();
    const tp = this.takeProfitValue();
    const dir = this.directionValue();
    if (!entry || !sl || !tp) return '—';
    const slDist = Math.abs(entry - sl);
    const tpDist = Math.abs(tp - entry);
    if (slDist <= 0 || tpDist <= 0) return '—';
    const isLong = dir === 1;
    const rMult = isLong
      ? (entry - sl) / (tp - entry)
      : (sl - entry) / (entry - tp);
    if (!Number.isFinite(rMult)) return '—';
    return (rMult >= 0 ? '+' : '') + rMult.toFixed(2) + 'R';
  });

  readonly rMultipleHint = computed(() => {
    const entry = this.entryPriceValue();
    const sl = this.stopLossValue();
    const tp = this.takeProfitValue();
    if (!entry || !sl || !tp) return 'Completá entrada, SL y TP para calcular.';
    return 'Ratio riesgo/beneficio (SL = 1R).';
  });

  readonly positionSizeHint = computed(() => {
    const tab = this.activeTab();
    if (tab === 1) {
      const sl = this.stopLossValue();
      const risk = this.riskPercentValue();
      const entry = this.entryPriceValue();
      const account = this.selectedAccount();
      if (!sl || !risk || !entry || !account) {
        return 'Editable — definí riesgo y SL para sugerir lotes.';
      }
      const slDist = Math.abs(entry - sl);
      if (slDist <= 0) return 'SL debe ser distinto al entry.';
      const accountBalance = (account.initialBalance) || 0;
      const leverage = (account.leverage && account.leverage > 0) ? account.leverage : 1;
      const pipValue = this.selectedInstrument()?.pipValue ?? 0;
      const contractSize = this.selectedInstrument()?.contractSize ?? 1;
      const decimals = this.selectedInstrument()?.decimalPlaces ?? 5;
      // Tamaño de posición = (riesgo $) / (slDist * contractSize) — visual only.
      const riskAmount = (risk / 100) * accountBalance * leverage;
      const lots = riskAmount / (slDist * contractSize * (pipValue || 1));
      if (!Number.isFinite(lots) || lots <= 0) return 'Editable — definí riesgo y SL para sugerir lotes.';
      return `Sugerido: ${lots.toFixed(decimals)} lotes (visual).`;
    }
    return 'Editable.';
  });

  readonly expectedPnLLabel = computed(() => {
    const tab = this.activeTab();
    if (tab === 1) {
      const entry = this.entryPriceValue();
      const tp = this.takeProfitValue();
      const volume = this.volumeValue();
      const instr = this.selectedInstrument();
      if (!entry || !tp || !volume || !instr) return '—';
      const contractSize = instr.contractSize ?? 1;
      const pipValue = instr.pipValue ?? 0;
      const dir = this.directionValue();
      const dist = Math.abs(tp - entry);
      const sign = dir === 1 ? 1 : -1;
      const pnl = sign * dist * volume * contractSize * (pipValue || 1);
      if (!Number.isFinite(pnl)) return '—';
      return (pnl >= 0 ? '+' : '') + pnl.toFixed(2);
    }
    return '—';
  });

  readonly expectedPnLHint = computed(() => {
    const tab = this.activeTab();
    if (tab === 1) {
      const entry = this.entryPriceValue();
      const tp = this.takeProfitValue();
      if (!entry || !tp) return 'Asumiendo que el TP se alcanza.';
      return 'Asume TP alcanzado. Visual, no se persiste.';
    }
    return '';
  });

  // ====== Auto-calculations (Binary) ======
  readonly payoutLabel = computed(() => {
    const instr = this.selectedInstrument();
    if (!instr) return '—';
    return (instr.payoutPercent * 100).toFixed(1);
  });

  readonly payoutHint = computed(() => {
    const instr = this.selectedInstrument();
    if (!instr) return 'Elegí un instrumento para autocompletar.';
    return 'Auto desde el instrumento.';
  });

  readonly potentialPnLLabel = computed(() => {
    const instr = this.selectedInstrument();
    const amount = this.amountValue();
    if (!instr || !amount) return '—';
    const win = amount * instr.payoutPercent;
    if (!Number.isFinite(win)) return '—';
    return (win >= 0 ? '+' : '') + win.toFixed(2);
  });

  readonly potentialPnLHint = computed(() => {
    const instr = this.selectedInstrument();
    const amount = this.amountValue();
    if (!instr || !amount) return 'Si gana: importe × payout.';
    const loss = -amount;
    return `Si gana: +${(amount * instr.payoutPercent).toFixed(2)} · Si pierde: ${loss.toFixed(2)}.`;
  });

  constructor() {
    // Manejo de visibilidad: rendered siempre true mientras visible=true o mientras
    // termina la animación de salida (280ms).
    effect(() => {
      const v = this.visible();
      if (v) {
        if (this.hideTimer) {
          clearTimeout(this.hideTimer);
          this.hideTimer = null;
        }
        this.rendered.set(true);
        this.resetForm();
        void this.loadData();
      } else if (this.rendered()) {
        if (this.hideTimer) clearTimeout(this.hideTimer);
        this.hideTimer = setTimeout(() => {
          this.rendered.set(false);
          this.hideTimer = null;
        }, SLIDE_ANIMATION_MS);
      }
    });

    // Auto-tab al seleccionar cuenta.
    effect(() => {
      const acc = this.selectedAccount();
      if (!acc) return;
      const tab = acc.marketType;
      if (this.activeTab() !== tab) {
        this.activeTab.set(tab);
        // Reset direction default por tab.
        const defaultDir: TradeDirection = 1;
        this.form.controls.direction.setValue(defaultDir);
      }
    });

    // Si el instrumento seleccionado no es compatible con el tab activo, limpiarlo.
    effect(() => {
      const instr = this.selectedInstrument();
      const tab = this.activeTab();
      if (!instr) return;
      const isForex = hasAssetClass(instr.assetClasses, 1);
      const isBinary = hasAssetClass(instr.assetClasses, 4);
      if (tab === 1 && !isForex) {
        this.form.controls.instrumentId.setValue('');
      } else if (tab === 2 && !isBinary) {
        this.form.controls.instrumentId.setValue('');
      }
    });
  }

  ngOnDestroy(): void {
    this.destroyed = true;
    if (this.hideTimer) clearTimeout(this.hideTimer);
    if (this.screenshotPreviewUrl()) {
      URL.revokeObjectURL(this.screenshotPreviewUrl()!);
    }
  }

  @HostListener('document:keydown.escape')
  onEscape(): void {
    if (this.visible() && !this.submitting()) {
      this.cancel();
    }
  }

  async loadData(): Promise<void> {
    // Cache-friendly: NO force. Si el cache está fresco (< 60s), no hace HTTP.
    // Solo el botón "Reintentar" usa force=true para skipear el cache.
    await Promise.all([
      this.accountState.load(),
      this.instrumentState.load(),
      // Slice 1b: position-size calculator needs the active profile.
      // The state is cache-friendly too; safe to load in parallel.
      this.riskProfile.load(),
    ]);
  }

  async retryData(): Promise<void> {
    // Force = true para skipear cache. Usado por el botón "Reintentar".
    await Promise.all([
      this.accountState.load(true),
      this.instrumentState.load(true),
    ]);
  }

  setTab(tab: TradeTab): void {
    if (this.submitting()) return;
    if (this.activeTab() === tab) return;
    this.activeTab.set(tab);
    // Reset direction default por tab (1 = long/call, 2 = short/put).
    this.form.controls.direction.setValue(1);
  }

  setDirection(dir: TradeDirection): void {
    if (this.submitting()) return;
    this.form.controls.direction.setValue(dir);
    this.form.controls.direction.markAsTouched();
  }

  // ===== Pre-trade checklist handlers (slice 1c.2) =====
  /** Stores the latest checklist payload so `submit()` can include it in the POST body. */
  onChecklistSubmission(payload: PreTradeChecklistPayload): void {
    this.checklistSubmission.set(payload);
    // Clear stale 422 error state — the user has just committed a new attempt.
    this.checklistErrorCode.set(null);
    this.checklistFieldErrors.set([]);
  }

  /** Clears the stored checklist payload (cancel button on the checklist component). */
  onChecklistCancelled(): void {
    this.checklistSubmission.set(null);
    this.checklistErrorCode.set(null);
    this.checklistFieldErrors.set([]);
  }

  // ===== Position-size calculator handlers (slice 1b) =====
  /**
   * Pre-populates the form's `volume` field with the calculator's output.
   * The trader can still override it manually — the calculator is informational.
   */
  onPositionSizeCalculated(result: PositionSizeCalcResult): void {
    const tab = this.activeTab();
    if (tab === 1) {
      this.form.controls.volume.setValue(result.volume);
    } else {
      // Binary tab uses `amount` (importe), not `volume` (lots).
      // The calculator returns base units which don't map directly to binary
      // "importe" — we leave the binary field alone in this slice.
      return;
    }
  }

  /**
   * The calculator's default stop-loss input is |entry - sl| from the form,
   * pre-populated so the user doesn't have to re-type it.
   */
  readonly defaultStopLossForCalc = computed<number>(() => {
    const entry = this.entryPriceValue();
    const sl = this.stopLossValue();
    if (!entry || !sl) return 0;
    const dist = Math.abs(entry - sl);
    return Number.isFinite(dist) && dist > 0 ? dist : 0;
  });

  async submit(): Promise<void> {
    if (!this.canSubmit()) {
      this.form.markAllAsTouched();
      return;
    }
    const acc = this.selectedAccount();
    const instr = this.selectedInstrument();
    if (!acc || !instr) {
      this.submitError.set('Seleccioná una cuenta y un instrumento válidos.');
      return;
    }

    this.submitting.set(true);
    this.submitError.set(null);
    try {
      // En binarias, "volume" se alimenta desde el campo "amount" (importe).
      const tab = this.activeTab();
      const rawVolume = this.form.controls.volume.value;
      const amount = this.form.controls.amount.value;
      const effectiveVolume = tab === 2 ? amount : rawVolume;

      const request: OpenTradeRequest = {
        accountId: acc.id,
        instrumentId: instr.id,
        symbol: instr.symbol,
        // Backend trade schema (legacy) aún usa el enum single-value:
        //   1 = Forex, 3 = Binary. Lo derivamos del marketType de la cuenta.
        assetClass: this.tradeAssetClassFor(acc.marketType),
        direction: this.form.controls.direction.value,
        volume: effectiveVolume,
        volumeCurrency: acc.currency,
        entryPrice: this.form.controls.entryPrice.value,
        entryPriceCurrency: this.entryPriceCurrency(),
        strategy: this.form.controls.strategy.value.trim() || null,
        notes: this.form.controls.notes.value.trim() || null,
        // Slice 1c.2: optional pre-trade checklist. Backend maps a failing
        // checklist to 422 with `validation.pre_trade_checklist.*`.
        checklist: this.checklistSubmission(),
      };
      const created = await this.tradeApi.open(request);
      this.saved.emit(created);
    } catch (e) {
      // Slice 1c.2: parse RFC 7807 problem for pre-trade checklist errors
      // and surface them to the checklist via its errorCode/fieldErrors inputs.
      const parsed = this.parseChecklistProblem(e);
      if (parsed) {
        this.checklistErrorCode.set(parsed.code);
        this.checklistFieldErrors.set(parsed.fields);
        this.submitError.set(parsed.message);
      } else {
        this.checklistErrorCode.set(null);
        this.checklistFieldErrors.set([]);
        this.submitError.set(this.toMessage(e));
      }
    } finally {
      this.submitting.set(false);
    }
  }

  cancel(): void {
    if (this.submitting()) return;
    this.cancelled.emit();
  }

  isInvalid(control: { invalid: boolean; touched: boolean }): boolean {
    return control.invalid && control.touched;
  }

  assetClassLabel(ac: AssetClass): string {
    return labelForAssetClass(ac);
  }

  /**
   * Etiqueta representativa para un instrumento (bitmask): prioriza el AssetClass
   * compatible con el tab activo, si existe. Si no, usa el primero activo.
   */
  assetClassLabelForInstrument(bitmask: number): string {
    const tabBit = this.activeTabBit();
    if (hasAssetClass(bitmask, tabBit)) return labelForAssetClass(tabBit);
    const first = activeAssetClassesFor(bitmask)[0];
    return first !== undefined ? labelForAssetClass(first) : '—';
  }

  /**
   * Mapea el MarketType de la cuenta al enum legacy single-value que el backend
   * todavía espera en `OpenTradeRequest.assetClass` (1=Forex, 3=Binary).
   */
  private tradeAssetClassFor(marketType: MarketType): 1 | 3 {
    return marketType === 1 ? 1 : 3;
  }

  marketTypeLabel(mt: MarketType): string {
    return MARKET_TYPE_LABELS[mt];
  }

  onScreenshotDragOver(event: DragEvent): void {
    event.preventDefault();
    event.stopPropagation();
    if (!this.screenshotDrag()) this.screenshotDrag.set(true);
  }

  onScreenshotDragLeave(event: DragEvent): void {
    event.preventDefault();
    event.stopPropagation();
    this.screenshotDrag.set(false);
  }

  onScreenshotDrop(event: DragEvent): void {
    event.preventDefault();
    event.stopPropagation();
    this.screenshotDrag.set(false);
    const file = event.dataTransfer?.files?.[0];
    if (file) this.acceptScreenshot(file);
  }

  onScreenshotPicked(event: Event): void {
    const input = event.target as HTMLInputElement;
    const file = input.files?.[0];
    if (file) this.acceptScreenshot(file);
  }

  clearScreenshot(event: Event): void {
    event.stopPropagation();
    event.preventDefault();
    this.screenshotFile = null;
    const url = this.screenshotPreviewUrl();
    if (url) URL.revokeObjectURL(url);
    this.screenshotPreviewUrl.set(null);
    this.screenshotName.set(null);
  }

  private acceptScreenshot(file: File): void {
    if (!file.type.startsWith('image/')) return;
    this.screenshotFile = file;
    this.screenshotName.set(file.name);
    const prev = this.screenshotPreviewUrl();
    if (prev) URL.revokeObjectURL(prev);
    this.screenshotPreviewUrl.set(URL.createObjectURL(file));
  }

  private resetForm(): void {
    this.form.reset({
      accountId: '',
      instrumentId: '',
      direction: 1,
      volume: 0,
      amount: 0,
      entryPrice: 0,
      exitPrice: null,
      stopLoss: null,
      takeProfit: null,
      riskPercent: null,
      strategy: '',
      notes: '',
      session: '',
      openedAt: this.nowLocalIso(),
      closedAt: null,
      expiresAt: null,
    });
    this.submitError.set(null);
    // Slice 1c.2: clear any leftover pre-trade checklist state from a
    // previous open of the dialog (success path emits `saved`, but if the
    // user cancels mid-edit or the API rejected, we don't want stale state).
    this.checklistSubmission.set(null);
    this.checklistErrorCode.set(null);
    this.checklistFieldErrors.set([]);
    // Determinar tab inicial según cuentas disponibles.
    if (this.forexAccounts().length > 0) {
      this.activeTab.set(1);
    } else if (this.binaryAccounts().length > 0) {
      this.activeTab.set(2);
    }
    // Limpiar screenshot.
    this.screenshotFile = null;
    const url = this.screenshotPreviewUrl();
    if (url) URL.revokeObjectURL(url);
    this.screenshotPreviewUrl.set(null);
    this.screenshotName.set(null);
  }

  private stepFor(dp: number): string {
    if (dp <= 0) return '1';
    return '0.' + '0'.repeat(dp - 1) + '1';
  }

  private nowLocalIso(): string {
    const now = new Date();
    const tzOffsetMs = now.getTimezoneOffset() * 60 * 1000;
    const local = new Date(now.getTime() - tzOffsetMs);
    return local.toISOString().slice(0, 16);
  }

  /**
   * Slice 1c.2: parses a 422 RFC 7807 problem coming from
   * `POST /api/trades` when the pre-trade checklist fails validation.
   * Returns null when the error is unrelated to the checklist so the caller
   * can fall back to the generic `toMessage` formatter.
   *
   * The backend's `ProblemFromResult` (slice 1c.1) emits:
   *   { type: "https://jadecapital/errors/<code>", title, detail,
   *     status: 422, code: "<code>", extensions?: { code, fields? } }
   *
   * We pull the `<code>` suffix out of `type` (last URL segment) and merge
   * `extensions.fields` into the fieldErrors array.
   */
  private parseChecklistProblem(error: unknown): { code: string; fields: string[]; message: string } | null {
    if (!(error instanceof HttpErrorResponse) || error.status !== 422) return null;
    const body = (error.error ?? {}) as {
      type?: string;
      code?: string;
      detail?: string;
      title?: string;
      extensions?: { code?: string; fields?: string[] };
    };

    // Backend's validation.pre_trade_checklist.* codes are what trigger 422 here.
    const candidate = body.code ?? body.extensions?.code ?? '';
    if (!candidate.startsWith('pre_trade_checklist.') && !candidate.startsWith('validation.pre_trade_checklist.')) {
      // Try the `type` URL: "https://jadecapital/errors/<code>"
      const fromType = body.type?.split('/').pop() ?? '';
      if (!fromType.startsWith('pre_trade_checklist.')) return null;
      const code = fromType;
      const fields = body.extensions?.fields ?? [];
      const message = body.detail ?? body.title ?? 'El checklist falló la validación.';
      return { code, fields, message };
    }

    const code = candidate.startsWith('validation.')
      ? candidate.slice('validation.'.length)
      : candidate;
    const fields = body.extensions?.fields ?? [];
    const message = body.detail ?? body.title ?? 'El checklist falló la validación.';
    return { code, fields, message };
  }

  private toMessage(error: unknown): string {
    if (error instanceof HttpErrorResponse) {
      if (error.status === 409) {
        const detail = error.error?.detail;
        if (typeof detail === 'string' && detail) return detail;
        return 'Conflicto al crear la operación (¿symbol duplicado o cuenta inactiva?).';
      }
      if (error.status === 400 && error.error?.errors) {
        const errors = error.error.errors as Record<string, unknown>;
        const messages: string[] = [];
        for (const field of Object.keys(errors)) {
          const fieldErrors = errors[field];
          if (Array.isArray(fieldErrors)) {
            const flat = fieldErrors
              .map(v => (typeof v === 'string' ? v : JSON.stringify(v)))
              .join(', ');
            messages.push(`${field}: ${flat}`);
          }
        }
        if (messages.length > 0) return messages.join(' · ');
      }
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