import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { RiskProfileDto } from '@core/api/risk-profile.types';
import { RiskProfileService } from '@core/api/risk-profile.service';
import {
  PreTradeChecklist,
  PreTradeChecklistPayload,
} from '../pre-trade-checklist';

// ============================================================================
//  PreTradeChecklist — slice 1c.2 frontend.
//
//  Three jest specs covering the slice contract:
//  1. Component renders with the expected DOM (5+5 pills, slider, RR inputs).
//  2. "Aceptar" emits a payload with the exact backend DTO shape.
//  3. A 422 from the parent surfaces both the top banner and the inline
//     `jcs-input--error` class on the failing input.
// ============================================================================

describe('PreTradeChecklist', () => {
  let api: { getActive: jest.Mock };

  const sampleDto = (overrides: Partial<RiskProfileDto> = {}): RiskProfileDto => ({
    id: '11111111-1111-1111-1111-111111111111',
    userId: '22222222-2222-2222-2222-222222222222',
    capitalAmount: 10_000,
    capitalCurrency: 'USD',
    maxDrawdownPercent: 10,
    riskPerTradePercent: 1,
    riskRewardTarget: 3,
    isActive: true,
    supersededAt: null,
    createdAt: '2026-08-15T10:00:00.000Z',
    updatedAt: '2026-08-15T10:00:00.000Z',
    ...overrides,
  });

  beforeEach(async () => {
    api = { getActive: jest.fn() };
    await TestBed.configureTestingModule({
      imports: [PreTradeChecklist],
      providers: [
        provideHttpClient(),
        { provide: RiskProfileService, useValue: api },
      ],
    }).compileComponents();
  });

  it('renders 5 emotionality pills + 5 setup pills, defaults confluences=5 and RR=2.0', async () => {
    api.getActive.mockReturnValueOnce(new Promise(() => {})); // profile never resolves

    const fixture = TestBed.createComponent(PreTradeChecklist);
    fixture.componentRef.setInput('errorCode', null);
    fixture.componentRef.setInput('fieldErrors', []);
    fixture.detectChanges();
    await Promise.resolve();
    fixture.detectChanges();

    const html = fixture.nativeElement as HTMLElement;

    // Five emotionality pills (data-value="1".."5") + five setup pills.
    const emoPills = html.querySelectorAll('[role="radiogroup"][aria-label="Estado emocional"] .ptc-pill');
    const setupPills = html.querySelectorAll('[role="radiogroup"][aria-label="Calidad del setup"] .ptc-pill');
    expect(emoPills.length).toBe(5);
    expect(setupPills.length).toBe(5);

    // Slider default.
    const slider = html.querySelector('input[type="range"]') as HTMLInputElement;
    expect(slider.value).toBe('5');

    // RR defaults: entry=2, target=2 (no profile yet so recommendedTarget=2 fallback).
    const rrEntry = html.querySelector('#ptc-rr-entry') as HTMLInputElement;
    const rrTarget = html.querySelector('#ptc-rr-target') as HTMLInputElement;
    expect(rrEntry.value).toBe('2');
    expect(rrTarget.value).toBe('2');

    // Recommended label is visible.
    expect(html.textContent).toContain('Recomendado:');
  });

  it('Aceptar emits the exact PreTradeChecklistPayload DTO shape', async () => {
    api.getActive.mockReturnValueOnce(new Promise(() => {}));

    const fixture = TestBed.createComponent(PreTradeChecklist);
    fixture.componentRef.setInput('errorCode', null);
    fixture.componentRef.setInput('fieldErrors', []);
    fixture.detectChanges();
    await Promise.resolve();
    fixture.detectChanges();

    const component = fixture.componentInstance;
    // selection
    component.setEmotionality(3);    // Neutral
    component.setSetupQuality(4);    // Good
    component.onConfluencesInput({ target: { valueAsNumber: 7 } } as unknown as Event);
    component.onRrEntryInput({ target: { valueAsNumber: 2.5 } } as unknown as Event);
    component.onRrTargetInput({ target: { valueAsNumber: 2.0 } } as unknown as Event);
    fixture.detectChanges();

    let captured: PreTradeChecklistPayload | undefined;
    component.submission.subscribe(p => { captured = p; });

    component.onAccept();

    expect(captured).toEqual({
      emotionality: 3,
      setupQuality: 4,
      riskRewardAtEntry: 2.5,
      riskRewardTargetUsed: 2.0,
      confluencesCount: 7,
    });
  });

  it('Aceptar is disabled until emotionality + setupQuality + confluences + both RR are valid', () => {
    api.getActive.mockReturnValueOnce(new Promise(() => {}));
    const fixture = TestBed.createComponent(PreTradeChecklist);
    fixture.componentRef.setInput('errorCode', null);
    fixture.componentRef.setInput('fieldErrors', []);
    fixture.detectChanges();
    const component = fixture.componentInstance;

    expect(component.isValid()).toBe(false);

    component.setEmotionality(3);
    component.setSetupQuality(4);
    // default confluences=5, RR entry=2, target=2 — should be valid now.
    expect(component.isValid()).toBe(true);

    // Break it: RR entry below 1.
    component.onRrEntryInput({ target: { valueAsNumber: 0.5 } } as unknown as Event);
    expect(component.isValid()).toBe(false);
  });

  it('renders the top banner + jcs-input--error class when errorCode + fieldErrors are provided', async () => {
    api.getActive.mockReturnValueOnce(new Promise(() => {}));

    const fixture = TestBed.createComponent(PreTradeChecklist);
    fixture.componentRef.setInput('errorCode', 'pre_trade_checklist.rr_below_target');
    fixture.componentRef.setInput('fieldErrors', ['riskRewardAtEntry']);
    fixture.detectChanges();
    await Promise.resolve();
    fixture.detectChanges();

    const html = fixture.nativeElement as HTMLElement;

    // Banner copy is shown.
    const banner = html.querySelector('[data-testid="checklist-banner"]') as HTMLElement;
    expect(banner).not.toBeNull();
    expect(banner.textContent).toContain('R/R insuficiente');

    // Inline error class on the RR entry input.
    const rrEntry = html.querySelector('#ptc-rr-entry') as HTMLInputElement;
    expect(rrEntry.classList.contains('jcs-input--error')).toBe(true);

    // The other RR input stays clean.
    const rrTarget = html.querySelector('#ptc-rr-target') as HTMLInputElement;
    expect(rrTarget.classList.contains('jcs-input--error')).toBe(false);
  });

  it('seeds the RR target from the active risk profile (riskRewardTarget)', async () => {
    api.getActive.mockResolvedValueOnce(sampleDto({ riskRewardTarget: 3 }));

    const fixture = TestBed.createComponent(PreTradeChecklist);
    fixture.componentRef.setInput('errorCode', null);
    fixture.componentRef.setInput('fieldErrors', []);
    fixture.detectChanges();

    // Wait several microtask cycles so the state's async load completes
    // and the component's effect picks up the new recommended target.
    for (let i = 0; i < 5; i++) await Promise.resolve();
    fixture.detectChanges();

    const component = fixture.componentInstance;
    expect(component.recommendedTarget()).toBe(3);
    expect(component.riskRewardTargetUsed()).toBe(3);

    // User edits the target → userTouchedTarget flips; further profile loads don't overwrite.
    component.onRrTargetInput({ target: { valueAsNumber: 4.5 } } as unknown as Event);
    expect(component.riskRewardTargetUsed()).toBe(4.5);
    expect(component.userTouchedTarget()).toBe(true);
  });
});
