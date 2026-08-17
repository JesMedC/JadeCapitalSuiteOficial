import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideRouter } from '@angular/router';
import { RiskAdviceService } from '../api/risk-advice.service';
import { RiskAdviceState } from '../state/risk-advice.state';
import { RiskAdvicePanel } from '../risk-advice-panel';

jest.spyOn(console, 'warn').mockImplementation(() => {});

describe('RiskAdvicePanel', () => {
  let fixture: ComponentFixture<RiskAdvicePanel>;
  let component: RiskAdvicePanel;
  let svcMock: jest.Mocked<RiskAdviceService>;
  let state: RiskAdviceState;

  beforeEach(async () => {
    svcMock = {
      getHealth: jest.fn(),
      requestAdvice: jest.fn(),
      getCachedAdvice: jest.fn(),
    } as unknown as jest.Mocked<RiskAdviceService>;

    await TestBed.configureTestingModule({
      imports: [RiskAdvicePanel],
      providers: [
        provideRouter([]),
        provideHttpClient(),
        { provide: RiskAdviceService, useValue: svcMock },
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(RiskAdvicePanel);
    component = fixture.componentInstance;
    state = TestBed.inject(RiskAdviceState);
    fixture.detectChanges();
  });

  it('renders the panel title', () => {
    const h3 = fixture.nativeElement.querySelector('.risk-advice-title');
    expect(h3?.textContent).toContain('AI Risk Advisor');
  });

  it('exposes the fire() helper method', () => {
    expect(typeof component.fire).toBe('function');
  });

  it('exposes the actionLabel helper', () => {
    expect(component.actionLabel('allow')).toBe('OK');
    expect(component.actionLabel('warning')).toBe('Cuidado');
    expect(component.actionLabel('block')).toBe('Bloquear');
  });

  it('renders the empty state when no advisory is loaded', () => {
    const empty = fixture.nativeElement.querySelector('.risk-advice-empty');
    expect(empty).toBeTruthy();
  });

  it('renders the warning card when state has an advisory with warning action', () => {
    state.currentAdvice.set({
      id: '11111111-1111-1111-1111-111111111111',
      userId: '22222222-2222-2222-2222-222222222222',
      tradeId: null,
      action: 'warning',
      reason: '4 losses in a row on EURUSD',
      model: 'llama3.1:8b',
      latencyMs: 412,
      createdAt: new Date().toISOString(),
    });
    fixture.detectChanges();

    const card = fixture.nativeElement.querySelector('.risk-advice-card');
    expect(card?.getAttribute('data-action')).toBe('warning');
    expect(card?.textContent).toContain('4 losses in a row');
  });

  it('renders the block card with the override warning copy', () => {
    state.currentAdvice.set({
      id: '11111111-1111-1111-1111-111111111111',
      userId: '22222222-2222-2222-2222-222222222222',
      tradeId: null,
      action: 'block',
      reason: 'excessive risk',
      model: 'llama3.1:8b',
      latencyMs: 412,
      createdAt: new Date().toISOString(),
    });
    fixture.detectChanges();

    const card = fixture.nativeElement.querySelector('.risk-advice-card');
    expect(card?.getAttribute('data-action')).toBe('block');
    expect(card?.textContent).toContain('AI recomienda no abrir este trade');
  });

  it('renders the loading state when the state is loading', () => {
    state.isLoading.set(true);
    fixture.detectChanges();

    const loading = fixture.nativeElement.querySelector('.risk-advice-loading');
    expect(loading).toBeTruthy();
  });
});
