import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideRouter, Router } from '@angular/router';
import { Component, signal } from '@angular/core';
import { CoachingPromptsComponent } from '../coaching-prompts.component';
import { CoachingPromptDto } from '../api/coaching.types';

// Suppress the harmless zone.js deprecation warning emitted by jest-preset-angular bootstrap.
jest.spyOn(console, 'warn').mockImplementation(() => {});

// ============================================================================
//  CoachingPromptsComponent — slice 2d.2 frontend tests.
//
//  Spec coverage (3):
//   1. Renders prompts: input 3 prompts (high, medium, low) → 3 cards in
//      severity desc order (high first, low last).
//   2. CTA click navigates: clicking the CTA button calls
//      Router.navigateByUrl with the prompt's cta.route.
//   3. Empty state: input [] → renders the "Sin prompts activos" copy.
//
//  Strict TDD: RED at the time of writing (the component is wired up
//  after the assertions land). GREEN once the implementation follows.
// ============================================================================

const HIGH_PROMPT: CoachingPromptDto = {
  ruleId: 'TiltSequence',
  severity: 'high',
  title: 'Posible tilt detectado',
  body: '3 pérdidas consecutivas en 90 min — posible tilt. Cierra el terminal.',
  cta: { route: '/app/trades', label: 'Revisar operaciones' },
  occurredAt: '2026-08-17T12:30:00.000Z',
};

const MEDIUM_PROMPT: CoachingPromptDto = {
  ruleId: 'OvertradingDay',
  severity: 'medium',
  title: 'Sobreoperativa',
  body: 'Operaste 14 veces hoy — tu promedio es 5.',
  cta: { route: '/app/patterns', label: 'Ver patrones' },
  occurredAt: '2026-08-16T18:00:00.000Z',
};

const LOW_PROMPT: CoachingPromptDto = {
  ruleId: 'LongBreak',
  severity: 'low',
  title: 'Racha sin operar',
  body: '5 días sin operar — ¿descanso intencional?',
  cta: { route: '/app/journal', label: 'Reflexionar' },
  occurredAt: '2026-08-10T09:00:00.000Z',
};

/** Wraps the prompt input in a signal so it can be passed to the
 *  signal-input API (Angular 18+). The test mutates prompts() on this
 *  fixture host to drive re-renders. */
@Component({
  standalone: true,
  imports: [CoachingPromptsComponent],
  template: `<jcs-coaching-prompts [prompts]="prompts()"></jcs-coaching-prompts>`,
})
class HarnessComponent {
  readonly prompts = signal<readonly CoachingPromptDto[]>([]);
}

describe('CoachingPromptsComponent', () => {
  async function setup(initial: readonly CoachingPromptDto[] = []) {
    await TestBed.configureTestingModule({
      imports: [HarnessComponent],
      providers: [provideHttpClient(), provideRouter([])],
    }).compileComponents();
    const fixture = TestBed.createComponent(HarnessComponent);
    fixture.componentInstance.prompts.set(initial);
    fixture.detectChanges();
    return { fixture, harness: fixture.componentInstance };
  }

  it('renders prompts: input 3 prompts (high, medium, low) → 3 cards in severity desc order', async () => {
    // Pass them OUT OF order on purpose — the component must sort them
    // server-agnostically so a future wire-order change doesn't break UX.
    const { fixture, harness } = await setup([LOW_PROMPT, HIGH_PROMPT, MEDIUM_PROMPT]);
    fixture.detectChanges();

    const html = fixture.nativeElement as HTMLElement;
    const cards = html.querySelectorAll('li.coaching-card');
    expect(cards.length).toBe(3);

    const ruleAttr = (i: number): string | null =>
      cards[i].getAttribute('data-testid');

    // High → Medium → Low (severity desc).
    expect(ruleAttr(0)).toBe('coaching-card-TiltSequence');
    expect(ruleAttr(1)).toBe('coaching-card-OvertradingDay');
    expect(ruleAttr(2)).toBe('coaching-card-LongBreak');

    // Severity class on the first card maps to sev-high.
    const firstCard = cards[0] as HTMLElement;
    expect(firstCard.classList.contains('sev-high')).toBe(true);

    // Sanity: rule badges show ruleId in a styled chip.
    expect(html.textContent).toContain('TiltSequence');

    harness.prompts.set([]); // cleanup
  });

  it('CTA click navigates via Router.navigateByUrl with the prompt cta.route', async () => {
    const { fixture, harness } = await setup([HIGH_PROMPT]);
    fixture.detectChanges();

    // The router is from provideRouter([]) so navigateByUrl is callable.
    const router = TestBed.inject(Router);
    const navigateSpy = jest.spyOn(router, 'navigateByUrl').mockResolvedValue(true);

    const cta = fixture.nativeElement.querySelector(
      '[data-testid="coaching-cta-TiltSequence"]',
    ) as HTMLButtonElement;
    expect(cta).not.toBeNull();
    cta.click();

    expect(navigateSpy).toHaveBeenCalledTimes(1);
    expect(navigateSpy).toHaveBeenCalledWith('/app/trades');

    harness.prompts.set([]); // cleanup
  });

  it('empty state: input [] → renders "Sin prompts activos" copy', async () => {
    const { fixture, harness } = await setup([]);
    fixture.detectChanges();

    const html = fixture.nativeElement as HTMLElement;
    expect(html.textContent).toContain('Sin prompts activos');
    expect(html.querySelector('[data-testid="coaching-list"]')).toBeNull();
    expect(html.querySelector('[data-testid="coaching-empty"]')).not.toBeNull();

    harness.prompts.set([]); // cleanup
  });
});
