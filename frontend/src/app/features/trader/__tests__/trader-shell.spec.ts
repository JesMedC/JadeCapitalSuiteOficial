import { Component } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { AuthState } from '@core/state/auth.state';
import { OllamaHealthInterval, AiProviderStatus } from '@core/realtime/ollama-health.interval';
import { signal } from '@angular/core';
import { TraderShell } from '../trader-shell';
import { AttachmentUsageBanner } from '../attachments/attachment-usage-banner';

jest.spyOn(console, 'warn').mockImplementation(() => {});

/**
 * No-op stub for the attachment usage banner — keeps the trader-shell
 * template from trying to resolve its real DI graph (state + service +
 * repository), which is irrelevant to the nav smoke.
 */
@Component({ selector: 'jcs-attachment-usage-banner', standalone: true, template: '' })
class StubAttachmentUsageBanner {}

/**
 * Stub for the Ollama health interval — returns a writable signal the
 * tests can flip to verify the badge label. start()/stop() are no-ops
 * so the test does not spawn a real setInterval.
 */
class StubOllamaHealthInterval {
  readonly status = signal<AiProviderStatus>('unknown');
  start(): void {}
  stop(): void {}
  pollNow(): Promise<void> { return Promise.resolve(); }
}

/**
 * Slice 4e — Phase 1 navItems wiring smoke.
 *
 * Per design.md 4e.1.1 the trader-shell must expose exactly 9 nav items in
 * a fixed order. Mobile-nav has horizontal scroll so this is the precedent
 * the rest of the slice builds on.
 *
 * Order (per spec):
 *   1. Dashboard
 *   2. Trades
 *   3. Journal
 *   4. Scanner
 *   5. Watchlist
 *   6. Quotes
 *   7. Strategies
 *   8. Alerts
 *   9. Planner
 */
describe('TraderShell — slice 4e 9-item nav', () => {
  let component: TraderShell;
  let fixture: ReturnType<typeof TestBed.createComponent<TraderShell>>;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [TraderShell],
      providers: [
        provideRouter([]),
        {
          provide: AuthState,
          useValue: {
            user: () => null,
            logout: () => undefined,
          },
        },
        { provide: OllamaHealthInterval, useClass: StubOllamaHealthInterval },
      ],
    })
      .overrideComponent(TraderShell, {
        remove: { imports: [AttachmentUsageBanner] },
        add: { imports: [StubAttachmentUsageBanner] },
      })
      .compileComponents();

    fixture = TestBed.createComponent(TraderShell);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  const expectedOrder: ReadonlyArray<{ label: string; path: string }> = [
    { label: 'Dashboard',  path: 'dashboard' },
    { label: 'Trades',     path: 'trades' },
    { label: 'Journal',    path: 'journal' },
    { label: 'Scanner',    path: 'scanner' },
    { label: 'Watchlist',  path: 'watchlist' },
    { label: 'Quotes',     path: 'quotes' },
    { label: 'Strategies', path: 'strategies' },
    { label: 'Alerts',     path: 'alerts' },
    { label: 'Planner',    path: 'planner' },
    { label: 'Imports',    path: 'imports' },
    { label: 'Risk Advisor', path: 'risk-advice' },
  ];

  it('exposes exactly 11 nav items', () => {
    expect(component.navItems.length).toBe(11);
  });

  it('matches the spec order (... Dashboard, Trades, Journal, Scanner, Watchlist, Quotes, Strategies, Alerts, Planner, Imports, Risk Advisor)', () => {
    expect(component.navItems.map(i => ({ label: i.label, path: i.path }))).toEqual(
      expectedOrder
    );
  });

  it('every path points to a known trader route prefix', () => {
    const knownPaths = new Set(expectedOrder.map(i => i.path));
    for (const item of component.navItems) {
      expect(knownPaths.has(item.path)).toBe(true);
    }
  });

  it('every label is non-empty and unique', () => {
    const labels = component.navItems.map(i => i.label);
    expect(labels.every(l => typeof l === 'string' && l.length > 0)).toBe(true);
    expect(new Set(labels).size).toBe(labels.length);
  });

  // ---- Slice 5c.2 — AI provider status badge ----

  it('renders the AI status badge with the initial "checking" label', () => {
    const el = fixture.nativeElement as HTMLElement;
    const badgeEl = el.querySelector('[data-testid="ai-status"]');
    expect(badgeEl).toBeTruthy();
    const text = badgeEl?.textContent?.trim() ?? '';
    expect(text.toLowerCase()).toContain('checking');
  });
});