import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideRouter } from '@angular/router';
import { HttpErrorResponse } from '@angular/common/http';
import { JournalEntryDto, UpsertJournalEntryRequest } from '../api/journal.types';
import { JournalService } from '../api/journal.service';
import { JournalState } from '../state/journal.state';
import { JournalPage } from '../journal-page';

// Suppress the harmless zone.js deprecation warning emitted by the jest-preset-angular bootstrap.
jest.spyOn(console, 'warn').mockImplementation(() => {});

// ============================================================================
//  JournalPage — slice 2a.2 frontend tests.
//
//  Spec coverage (4 user-required specs):
//   1. Renders the empty state copy when the API returns 404 on load.
//   2. Save success: shows the "Guardado HH:mm" banner after a successful upsert.
//   3. Validation errors: 422 surfaces the error banner with the backend detail.
//   4. Delete confirmation: removing the entry returns the page to the empty state.
//
//  Strict TDD: these tests reference production code (JournalPage, JournalState,
//  JournalService) that does not exist yet at the time of writing — they are
//  RED. GREEN happens once Phase 1 (service + state) + Phase 2 (page) land.
// ============================================================================

describe('JournalPage', () => {
  let api: { getToday: jest.Mock; upsertToday: jest.Mock; delete: jest.Mock; getRange: jest.Mock };
  let state: JournalState;

  const sampleEntry = (overrides: Partial<JournalEntryDto> = {}): JournalEntryDto => ({
    id: '11111111-1111-1111-1111-111111111111',
    localDate: '2026-08-16',
    timezone: 'America/Argentina/Buenos_Aires',
    moodPre: 4,
    moodDuring: 3,
    moodPost: 5,
    premarketPlan: 'Esperar ruptura de 1.0870 con volumen.',
    postmarketReflection: 'Buena ejecución. Respeté el plan.',
    tags: ['disciplina', 'buen-setup'],
    createdAt: '2026-08-16T13:00:00.000Z',
    updatedAt: '2026-08-16T18:30:00.000Z',
    ...overrides,
  });

  /** Mirrors the real service signature (Promise-returning). */
  const fakeApi = (): { getToday: jest.Mock; upsertToday: jest.Mock; delete: jest.Mock; getRange: jest.Mock } => ({
    getToday: jest.fn().mockResolvedValue(null),
    upsertToday: jest.fn().mockResolvedValue(sampleEntry()),
    delete: jest.fn().mockResolvedValue(undefined),
    getRange: jest.fn().mockResolvedValue([]),
  });

  beforeEach(async () => {
    api = fakeApi();

    await TestBed.configureTestingModule({
      imports: [JournalPage],
      providers: [
        provideHttpClient(),
        provideRouter([]),
        JournalState,
        { provide: JournalService, useValue: api },
      ],
    }).compileComponents();

    // Reset state between tests so signal leakage from earlier tests
    // (e.g. a leftover lastSavedAt) does not pollute later assertions.
    state = TestBed.inject(JournalState);
    state.reset();
  });

  /** Flushes microtasks + change-detection. Mirrors the pattern other
   *  slice-1 specs use (`Promise.resolve()` × 2 + `detectChanges()`). */
  async function settle(fixture: { detectChanges: () => void }) {
    fixture.detectChanges();
    await Promise.resolve();
    await Promise.resolve();
    fixture.detectChanges();
  }

  it('renders the empty state copy when the API returns 404 on load', async () => {
    // 404 is the only way `getToday` communicates "no entry yet". The state
    // service must collapse that to `entry = null` SILENTLY (no error banner),
    // and the page must render its empty-state copy.
    api.getToday.mockRejectedValue(httpError(404));

    const fixture = TestBed.createComponent(JournalPage);
    await settle(fixture);

    const state = TestBed.inject(JournalState);
    // Debug: confirm the mock was called and the state actually resolved.
    expect(api.getToday).toHaveBeenCalled();
    expect(state.error()).toBeNull();
    expect(state.entry()).toBeNull();
    expect(state.hasEntry()).toBe(false);

    const html = fixture.nativeElement as HTMLElement;
    expect(html.textContent).toContain('Aún no escribiste hoy');
    expect(html.textContent).toContain('¿Cómo te sentís');
    // The delete button is hidden when there is no entry.
    expect(html.querySelector('[data-testid="journal-delete"]')).toBeNull();
  });

  it('save success: shows the "Guardado HH:mm" banner after a successful upsert', async () => {
    api.getToday.mockRejectedValue(httpError(404));
    const saved = sampleEntry({ updatedAt: '2026-08-16T18:30:00.000Z' });
    api.upsertToday.mockResolvedValue(saved);

    const fixture = TestBed.createComponent(JournalPage);
    await settle(fixture);

    const component = fixture.componentInstance;
    component.moodPre.set(4);
    component.premarketPlan.set('Setup claro.');
    await component.onSave();
    fixture.detectChanges();

    expect(api.upsertToday).toHaveBeenCalledTimes(1);
    const payload: UpsertJournalEntryRequest = api.upsertToday.mock.calls[0][0];
    expect(payload.moodPre).toBe(4);
    expect(payload.premarketPlan).toBe('Setup claro.');

    const html = fixture.nativeElement as HTMLElement;
    expect(html.textContent).toContain('Guardado');
    // HH:mm format (e.g. "10:30")
    expect(html.textContent).toMatch(/Guardado\s+\d{2}:\d{2}/);
  });

  it('validation errors: 422 surfaces the error banner with the backend detail', async () => {
    api.getToday.mockRejectedValue(httpError(404));
    api.upsertToday.mockRejectedValue(httpError(422, { detail: 'Plan demasiado largo' }));

    const fixture = TestBed.createComponent(JournalPage);
    await settle(fixture);

    const component = fixture.componentInstance;
    // Force the form over the cap so the upsert path runs (the backend is
    // the source of truth for cap messages — the textarea `maxlength` is
    // bypassed here because we mutate the signal directly).
    component.premarketPlan.set('x'.repeat(2001));
    await component.onSave();
    fixture.detectChanges();

    const html = fixture.nativeElement as HTMLElement;
    expect(html.textContent).toContain('Plan demasiado largo');
    // The plan counter must overflow and show the error variant.
    const counter = html.querySelector('[data-testid="plan-counter"]');
    expect(counter).not.toBeNull();
    expect(counter?.classList.contains('jcs-counter--over')).toBe(true);
  });

  it('delete confirmation: removing the entry returns the page to the empty state', async () => {
    api.getToday.mockResolvedValue(sampleEntry());
    api.delete.mockResolvedValue(undefined);

    const fixture = TestBed.createComponent(JournalPage);
    await settle(fixture);

    const component = fixture.componentInstance;
    // Spy on confirm so the test never blocks on a native dialog.
    jest.spyOn(window, 'confirm').mockReturnValue(true);

    const html = fixture.nativeElement as HTMLElement;
    // The hydrated entry must show up in the form textarea (not in textContent).
    const planArea = html.querySelector('#jp-plan') as HTMLTextAreaElement;
    expect(planArea).not.toBeNull();
    expect(planArea.value).toContain('Esperar ruptura de 1.0870');
    expect(html.querySelector('[data-testid="journal-delete"]')).not.toBeNull();

    await component.onDelete();
    fixture.detectChanges();

    expect(api.delete).toHaveBeenCalledWith('11111111-1111-1111-1111-111111111111');
    // After the delete resolves, the state clears the entry and the empty-state
    // copy returns.
    expect(html.textContent).toContain('Aún no escribiste hoy');
  });
});

function httpError(status: number, body: unknown = {}): HttpErrorResponse {
  return new HttpErrorResponse({ status, statusText: String(status), error: body });
}
