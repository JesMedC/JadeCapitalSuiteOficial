import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideRouter } from '@angular/router';
import { RiskProfileDto } from '@core/api/risk-profile.types';
import { RiskProfileService } from '@core/api/risk-profile.service';
import { RiskProfileState } from '@core/state/risk-profile.state';
import { RiskProfileTab } from '../risk-profile-tab';

describe('RiskProfileTab', () => {
  let api: { getActive: jest.Mock; upsert: jest.Mock };
  let state: RiskProfileState;

  const sampleDto = (overrides: Partial<RiskProfileDto> = {}): RiskProfileDto => ({
    id: '11111111-1111-1111-1111-111111111111',
    userId: '22222222-2222-2222-2222-222222222222',
    capitalAmount: 10_000,
    capitalCurrency: 'USD',
    maxDrawdownPercent: 10,
    riskPerTradePercent: 1,
    riskRewardTarget: 2,
    isActive: true,
    supersededAt: null,
    createdAt: '2026-08-15T10:00:00.000Z',
    updatedAt: '2026-08-15T10:00:00.000Z',
    ...overrides,
  });

  beforeEach(async () => {
    api = { getActive: jest.fn(), upsert: jest.fn() };
    await TestBed.configureTestingModule({
      imports: [RiskProfileTab],
      providers: [
        provideHttpClient(),
        provideRouter([]),
        { provide: RiskProfileService, useValue: api },
      ],
    }).compileComponents();
    state = TestBed.inject(RiskProfileState);
  });

  it('initial load with no profile shows the empty state copy', async () => {
    api.getActive.mockResolvedValueOnce(null);
    const fixture = TestBed.createComponent(RiskProfileTab);
    fixture.detectChanges();
    await Promise.resolve();
    await Promise.resolve();
    fixture.detectChanges();

    const html = fixture.nativeElement as HTMLElement;
    expect(html.textContent).toContain('Aún no configuraste tu perfil');
    expect(html.textContent).toContain('Creá uno para habilitar el trading con riesgo controlado.');
  });

  it('save success: pre-populates the form, submits, and shows the success banner', async () => {
    api.getActive.mockResolvedValueOnce(sampleDto());
    api.upsert.mockResolvedValueOnce({
      ...sampleDto(),
      riskPerTradePercent: 1.5,
      updatedAt: '2026-08-15T10:05:00.000Z',
    });

    const fixture = TestBed.createComponent(RiskProfileTab);
    fixture.detectChanges();
    await Promise.resolve();
    await Promise.resolve();
    fixture.detectChanges();

    // Tab should render the form with the profile values.
    const component = fixture.componentInstance;
    expect(component.form.controls.riskPerTradePercent.value).toBe(1);

    // Trigger submit with a valid value.
    component.form.controls.riskPerTradePercent.setValue(1.5);
    await component.onSubmit();
    fixture.detectChanges();

    expect(api.upsert).toHaveBeenCalledWith({
      capitalAmount: 10_000,
      capitalCurrency: 'USD',
      maxDrawdownPercent: 10,
      riskPerTradePercent: 1.5,
      riskRewardTarget: 2,
    });

    const html = fixture.nativeElement as HTMLElement;
    expect(html.textContent).toContain('Perfil guardado.');
  });

  it('validation error: 422 surfaces the field-level error and the error banner', async () => {
    api.getActive.mockResolvedValueOnce(sampleDto());
    api.upsert.mockRejectedValueOnce({
      status: 422,
      code: 'validation.risk_profile.risk_per_trade_percent_out_of_range',
      message: 'Risk-per-trade percent must be between 0.01 and 5.00.',
      field: 'riskPerTradePercent',
    });

    const fixture = TestBed.createComponent(RiskProfileTab);
    fixture.detectChanges();
    await Promise.resolve();
    await Promise.resolve();
    fixture.detectChanges();

    const component = fixture.componentInstance;
    // Submit a value that passes frontend validation (1.5) but the backend
    // still rejects (e.g. concurrent change tightened the range). The test
    // proves the 422 handling path, not the validator.
    component.form.controls.riskPerTradePercent.setValue(1.5);
    await component.onSubmit();
    fixture.detectChanges();

    const html = fixture.nativeElement as HTMLElement;
    // Field-level error message visible.
    expect(html.textContent).toContain('Risk-per-trade percent must be between 0.01 and 5.00.');
    // The field input carries the error class.
    const rptInput = html.querySelector('#rpf-rpt') as HTMLInputElement;
    expect(rptInput.classList.contains('jcs-input--error')).toBe(true);
    // Existing profile stays visible (no clearing).
    expect(component.state.profile()).toEqual(sampleDto());
  });

  it('409 conflict: surfaces the conflict-specific copy and the error banner', async () => {
    api.getActive.mockResolvedValueOnce(sampleDto());
    // Simulate the backend's default detail for a concurrent-supersede conflict.
    api.upsert.mockRejectedValueOnce({
      status: 409,
      code: 'conflict.risk_profile.concurrent_supersede',
      message: 'Otro proceso actualizó tu perfil. Recargá e intentá de nuevo.',
    });

    const fixture = TestBed.createComponent(RiskProfileTab);
    fixture.detectChanges();
    await Promise.resolve();
    await Promise.resolve();
    fixture.detectChanges();

    const component = fixture.componentInstance;
    await component.onSubmit();
    fixture.detectChanges();

    const html = fixture.nativeElement as HTMLElement;
    expect(html.textContent).toContain('Otro proceso actualizó tu perfil. Recargá e intentá de nuevo.');
    expect(component.state.profile()).toEqual(sampleDto());
  });
});
