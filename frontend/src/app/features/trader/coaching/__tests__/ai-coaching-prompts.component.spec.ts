import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideRouter } from '@angular/router';
import { Component, signal } from '@angular/core';
import { AiCoachingPromptsComponent } from '../ai-coaching-prompts.component';
import { AiCoachingPromptDto } from '../api/coaching.types';

jest.spyOn(console, 'warn').mockImplementation(() => {});

// ============================================================================
//  AiCoachingPromptsComponent — slice 5b.2 frontend tests.
//
//  Spec coverage (2):
//   1. Empty state: input [] → renders the "We'll generate your first AI
//      prompt overnight — keep trading." copy.
//   2. Renders prompts: input 3 prompts (high, medium, low) → 3 cards in
//      severity desc order (high first, low last).
// ============================================================================

/** Wraps the prompt input in a signal so it can be passed to the
 *  signal-input API (Angular 18+). The test mutates prompts() on this
 *  fixture host to drive re-renders. */
@Component({
  selector: 'jcs-host-shell',
  standalone: true,
  imports: [AiCoachingPromptsComponent],
  template: `<jcs-ai-coaching-prompts [prompts]="prompts()" />`,
})
class HarnessComponent {
  readonly prompts = signal<readonly AiCoachingPromptDto[]>([]);
}

describe('AiCoachingPromptsComponent (slice 5b.2)', () => {
  async function setup(initial: readonly AiCoachingPromptDto[] = []) {
    await TestBed.configureTestingModule({
      imports: [HarnessComponent],
      providers: [provideHttpClient(), provideRouter([])],
    }).compileComponents();
    const fixture = TestBed.createComponent(HarnessComponent);
    fixture.componentInstance.prompts.set(initial);
    fixture.detectChanges();
    return { fixture, harness: fixture.componentInstance };
  }

  it('renders empty-state copy when prompts is empty', async () => {
    const { fixture } = await setup([]);

    const html = fixture.nativeElement as HTMLElement;
    expect(html.querySelector('[data-testid="ai-coaching-empty"]')).toBeTruthy();
    expect(html.textContent).toContain('overnight');
    expect(html.querySelector('[data-testid="ai-coaching-list"]')).toBeNull();
  });

  it('renders 3 cards sorted high → medium → low with severity badge', async () => {
    const { fixture } = await setup([
      buildPrompt({ id: 'low', severity: 'low', text: 'low copy' }),
      buildPrompt({ id: 'high', severity: 'high', text: 'high copy' }),
      buildPrompt({ id: 'medium', severity: 'medium', text: 'medium copy' }),
    ]);

    const html = fixture.nativeElement as HTMLElement;
    const cards = html.querySelectorAll('[data-testid^="ai-coaching-card-"]');
    expect(cards.length).toBe(3);
    expect(cards[0].getAttribute('data-testid')).toBe('ai-coaching-card-high');
    expect(cards[1].getAttribute('data-testid')).toBe('ai-coaching-card-medium');
    expect(cards[2].getAttribute('data-testid')).toBe('ai-coaching-card-low');

    const bodies = html.querySelectorAll('[data-testid="ai-coaching-body"]');
    expect(bodies[0].textContent).toContain('high copy');
    expect(bodies[1].textContent).toContain('medium copy');
    expect(bodies[2].textContent).toContain('low copy');
  });
});

function buildPrompt(overrides: Partial<AiCoachingPromptDto>): AiCoachingPromptDto {
  return {
    id: 'id',
    kind: 'ai',
    severity: 'low',
    model: 'llama3.1:8b',
    latencyMs: 412,
    text: 'sample copy',
    providerResponse: '{"response": "sample copy"}',
    promptText: 'You are a trading coach…',
    contextJson: '{}',
    createdAt: new Date().toISOString(),
    cta: { route: '/app/journal', label: 'Revisar journal' },
    ...overrides,
  };
}