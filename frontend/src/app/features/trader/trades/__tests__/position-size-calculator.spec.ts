import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideRouter } from '@angular/router';
import {
  PositionSizeCalcResult,
  PositionSizeService,
} from '@core/api/position-size.service';
import { PositionSizeCalculator } from '../position-size-calculator';
import { RiskProfileDto } from '@core/api/risk-profile.types';

// ============================================================================
//  PositionSizeCalculator — slice 1b frontend.
//
//  Three jest specs covering the contract:
//  1. Renders inputs + calls the service on "Calcular", displays the volume.
//  2. When the profile is null, the component shows the
//     "Configurá tu perfil de riesgo primero" empty state copy.
//  3. When the service returns a valid DTO, the volume + riskAmount are
//     displayed and a `calculated` OutputEvent emits the DTO.
//
//  The component is intentionally dumb: it does not load the risk profile by
//  itself — the parent (create-trade-form) injects the active profile as an
//  input. This mirrors the pre-trade-checklist pattern (slice 1c.2).
// ============================================================================

describe('PositionSizeCalculator', () => {
  let api: { calculate: jest.Mock };

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

  const sampleCalcResult = (overrides: Partial<PositionSizeCalcResult> = {}): PositionSizeCalcResult => ({
    volume: 20_000,
    riskAmount: 100,
    riskPerTradePercent: 1,
    currency: 'USD',
    calculation: '(10000 × 1% / 100) / 0.0050 = 20000',
    recommendedStopLossDistance: null,
    ...overrides,
  });

  beforeEach(async () => {
    api = { calculate: jest.fn() };
    await TestBed.configureTestingModule({
      imports: [PositionSizeCalculator],
      providers: [
        provideHttpClient(),
        provideRouter([]),
        { provide: PositionSizeService, useValue: api },
      ],
    }).compileComponents();
  });

  it('renders the calculator with profile fields pre-populated and calls the service on Calcular', async () => {
    const profile = sampleDto({ capitalAmount: 10_000, riskPerTradePercent: 1 });
    api.calculate.mockResolvedValue(sampleCalcResult());

    const fixture = TestBed.createComponent(PositionSizeCalculator);
    fixture.componentRef.setInput('profile', profile);
    fixture.componentRef.setInput('defaultStopLossDistance', 0.005);
    fixture.detectChanges();
    await Promise.resolve();
    fixture.detectChanges();

    const html = fixture.nativeElement as HTMLElement;

    // Capital pre-populated read-only.
    const capitalField = html.querySelector('[data-testid="capital-amount"]') as HTMLElement;
    expect(capitalField).not.toBeNull();
    expect(capitalField.textContent).toContain('10000');

    // Risk percent from the profile (editable).
    const riskInput = html.querySelector('[data-testid="risk-percent"]') as HTMLInputElement;
    expect(riskInput).not.toBeNull();
    expect(riskInput.value).toBe('1');

    // Currency from the profile.
    expect(html.textContent).toContain('USD');

    // StopLoss input pre-populated from the parent's default.
    const slInput = html.querySelector('[data-testid="stop-loss-distance"]') as HTMLInputElement;
    expect(slInput).not.toBeNull();
    expect(slInput.value).toBe('0.005');

    // Calcular button calls the service.
    const calcButton = html.querySelector('[data-testid="calc-button"]') as HTMLButtonElement;
    expect(calcButton).not.toBeNull();
    calcButton.click();
    // onCalculate() is async (awaits service.calculate which uses firstValueFrom).
    // Flush several microtask cycles so the Promise chain resolves before assertions.
    for (let i = 0; i < 5; i++) await Promise.resolve();
    fixture.detectChanges();

    expect(api.calculate).toHaveBeenCalledTimes(1);
    const req = api.calculate.mock.calls[0][0];
    expect(req.stopLossDistance).toBe(0.005);
    expect(req.currency).toBe('USD');
    expect(req.riskPerTradeOverride).toBeNull();

    // Result panel renders the volume + riskAmount.
    expect(html.textContent).toContain('20000');
    expect(html.textContent).toContain('100');
  });

  it('shows the empty-state copy when the profile is null (no active profile)', () => {
    const fixture = TestBed.createComponent(PositionSizeCalculator);
    fixture.componentRef.setInput('profile', null);
    fixture.componentRef.setInput('defaultStopLossDistance', 0.005);
    fixture.detectChanges();

    const html = fixture.nativeElement as HTMLElement;
    const empty = html.querySelector('[data-testid="empty-state"]') as HTMLElement;
    expect(empty).not.toBeNull();
    expect(empty.textContent).toContain('Configurá tu perfil de riesgo primero');

    // The calc button / inputs must NOT be present in empty state.
    const calcButton = html.querySelector('[data-testid="calc-button"]');
    expect(calcButton).toBeNull();
  });

  it('emits the calculated OutputEvent when the service returns a valid DTO', async () => {
    const profile = sampleDto();
    const dto = sampleCalcResult();
    api.calculate.mockResolvedValue(dto);

    const fixture = TestBed.createComponent(PositionSizeCalculator);
    fixture.componentRef.setInput('profile', profile);
    fixture.componentRef.setInput('defaultStopLossDistance', 0.005);
    fixture.detectChanges();

    const component = fixture.componentInstance;
    let emitted: PositionSizeCalcResult | undefined;
    component.calculated.subscribe(r => { emitted = r; });

    await component.onCalculate();

    expect(api.calculate).toHaveBeenCalledTimes(1);
    expect(emitted).toEqual(dto);
  });
});
