import {
  ChangeDetectionStrategy,
  Component,
  computed,
  effect,
  inject,
  signal,
} from '@angular/core';
import { JournalState } from './state/journal.state';
import {
  JournalEntryDto,
  MOOD_LABELS,
  Mood,
  PLAN_MAX,
  REFLECTION_MAX,
  TAG_LEN_MAX,
  TAGS_MAX,
  UpsertJournalEntryRequest,
} from './api/journal.types';

// ============================================================================
//  JournalPage — slice 2a.2 frontend.
//
//  Mobile-first standalone page (Signals + OnPush + SCSS).
//
//  Layout (top → bottom):
//   - Header: title "Diario de hoy" + subtitle (localDate · timezone).
//   - Mood pills: 3 rows × 5 buttons (pre / during / post). Single-select
//     per row; tap the active button to clear.
//   - Pre-market plan: textarea, 2000 chars with visible counter.
//   - Post-market reflection: textarea, 5000 chars with visible counter.
//   - Tags: text input + chip list. tag ≤ 32 chars, max 10 tags.
//   - Actions: "Guardar diario" (primary), "Borrar entrada de hoy" (danger,
//     visible only when hasEntry()).
//
//  State transitions:
//   - isLoading()  → spinner.
//   - error()      → red banner with formatError().
//   - lastSavedAt()→ green banner "Guardado HH:mm".
//   - empty        → copy "Aún no escribiste hoy. ¿Cómo te sentís?".
//
//  All form state is local to the component. The page is the only writer
//  for `state.entry` — on load it hydrates the form, on save it stays in
//  sync via the signal update.
// ============================================================================

@Component({
  selector: 'jcs-journal-page',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="jp-page">
      <!-- ============== Header ============== -->
      <header class="jp-head">
        <p class="jcs-muted jp-eyebrow">Reflexión diaria</p>
        <h1 class="jp-title">Diario de hoy</h1>
        <p class="jcs-muted jp-sub">{{ headerDate() }} · {{ headerTz() }}</p>
      </header>

      <!-- ============== Error banner ============== -->
      @if (state.error(); as err) {
        <div class="jp-error" role="alert" data-testid="journal-error">
          <span>{{ err }}</span>
          <button type="button" class="jp-error-dismiss" (click)="state.clearError()">×</button>
        </div>
      }

      <!-- ============== Saved banner ============== -->
      @if (state.lastSavedAt(); as ts) {
        <div class="jp-saved" role="status" data-testid="journal-saved">
          Guardado {{ savedLabel(ts) }}
        </div>
      }

      <!-- ============== Loading ============== -->
      @if (state.isLoading()) {
        <div class="jp-loading" aria-live="polite">
          <span class="jp-spinner" aria-hidden="true"></span>
          <span class="jcs-muted">Cargando entrada de hoy…</span>
        </div>
      }

      <!-- ============== Empty state ============== -->
      @if (!state.isLoading() && !state.hasEntry() && !state.error()) {
        <div class="jp-empty" data-testid="journal-empty">
          <p>Aún no escribiste hoy. ¿Cómo te sentís?</p>
        </div>
      }

      <!-- ============== Form ============== -->
      <form class="jp-form" (submit)="$event.preventDefault(); onSave()" data-testid="journal-form">
        <!-- Mood pre -->
        <section class="jp-section">
          <h2 class="jp-section-title">Antes del mercado</h2>
          <div class="jp-pills" role="radiogroup" aria-label="Estado emocional pre-mercado">
            @for (m of MOODS; track m) {
              <button
                type="button"
                class="jp-pill"
                [class.jp-pill--active]="moodPre() === m"
                (click)="toggleMood('pre', m)"
                [attr.aria-pressed]="moodPre() === m">
                {{ MOOD_LABELS[m] }}
              </button>
            }
          </div>
        </section>

        <!-- Mood during -->
        <section class="jp-section">
          <h2 class="jp-section-title">Durante el mercado</h2>
          <div class="jp-pills" role="radiogroup" aria-label="Estado emocional durante el mercado">
            @for (m of MOODS; track m) {
              <button
                type="button"
                class="jp-pill"
                [class.jp-pill--active]="moodDuring() === m"
                (click)="toggleMood('during', m)"
                [attr.aria-pressed]="moodDuring() === m">
                {{ MOOD_LABELS[m] }}
              </button>
            }
          </div>
        </section>

        <!-- Mood post -->
        <section class="jp-section">
          <h2 class="jp-section-title">Después del mercado</h2>
          <div class="jp-pills" role="radiogroup" aria-label="Estado emocional post-mercado">
            @for (m of MOODS; track m) {
              <button
                type="button"
                class="jp-pill"
                [class.jp-pill--active]="moodPost() === m"
                (click)="toggleMood('post', m)"
                [attr.aria-pressed]="moodPost() === m">
                {{ MOOD_LABELS[m] }}
              </button>
            }
          </div>
        </section>

        <!-- Plan -->
        <section class="jp-section">
          <label class="jcs-label" for="jp-plan">Plan pre-mercado</label>
          <textarea
            id="jp-plan"
            class="jcs-input jp-area"
            rows="6"
            [attr.maxlength]="PLAN_MAX"
            [value]="premarketPlan()"
            (input)="onPlanChange($any($event.target).value)"
            placeholder="Setup, watchlist, escenarios, criterios de salida…"></textarea>
          <span
            class="jp-counter"
            [class.jcs-counter--over]="premarketPlan().length > PLAN_MAX"
            data-testid="plan-counter">
            {{ premarketPlan().length }} / {{ PLAN_MAX }}
          </span>
        </section>

        <!-- Reflection -->
        <section class="jp-section">
          <label class="jcs-label" for="jp-reflection">Reflexión post-mercado</label>
          <textarea
            id="jp-reflection"
            class="jcs-input jp-area"
            rows="8"
            [attr.maxlength]="REFLECTION_MAX"
            [value]="postmarketReflection()"
            (input)="onReflectionChange($any($event.target).value)"
            placeholder="Qué funcionó, qué mejorar, lecciones…"></textarea>
          <span
            class="jp-counter"
            [class.jcs-counter--over]="postmarketReflection().length > REFLECTION_MAX"
            data-testid="reflection-counter">
            {{ postmarketReflection().length }} / {{ REFLECTION_MAX }}
          </span>
        </section>

        <!-- Tags -->
        <section class="jp-section">
          <label class="jcs-label" for="jp-tag">Tags</label>
          <div class="jp-tags-row">
            <input
              id="jp-tag"
              class="jcs-input jp-tag-input"
              type="text"
              [attr.maxlength]="TAG_LEN_MAX"
              [value]="tagDraft()"
              (input)="onTagDraftChange($any($event.target).value)"
              (keydown.enter)="$event.preventDefault(); addTag()"
              placeholder="fomo, revenge, good-execution…" />
            <button
              type="button"
              class="jcs-btn jcs-btn--ghost jcs-btn--sm"
              (click)="addTag()"
              [disabled]="!canAddTag()">Añadir</button>
          </div>
          <ul class="jp-chips" aria-label="Tags del diario">
            @for (t of tags(); track t; let i = $index) {
              <li>
                <button
                  type="button"
                  class="jp-chip"
                  (click)="removeTag(i)"
                  [attr.aria-label]="'Quitar tag ' + t">{{ t }} ×</button>
              </li>
            }
          </ul>
        </section>

        <!-- Actions -->
        <div class="jp-actions">
          <button
            type="submit"
            class="jcs-btn jcs-btn--primary jp-save"
            [disabled]="state.isSaving() || !canSave()"
            data-testid="journal-save">
            @if (state.isSaving()) { Guardando… } @else { Guardar diario }
          </button>
          @if (state.hasEntry()) {
            <button
              type="button"
              class="jcs-btn jcs-btn--ghost jp-delete"
              (click)="onDelete()"
              [disabled]="state.isSaving()"
              data-testid="journal-delete">
              Borrar entrada de hoy
            </button>
          }
        </div>
      </form>
    </div>
  `,
  styles: [`
    :host { display: block; }

    .jp-page {
      display: flex;
      flex-direction: column;
      gap: var(--sp-5);
      max-width: 760px;
      animation: jp-fade 0.4s ease-out;
    }
    @keyframes jp-fade {
      from { opacity: 0; transform: translateY(6px); }
      to   { opacity: 1; transform: translateY(0); }
    }

    .jp-head { display: flex; flex-direction: column; gap: 4px; }
    .jp-eyebrow { font-size: var(--fs-sm); margin: 0; font-family: var(--font-mono); }
    .jp-title { font-size: var(--fs-3xl); font-weight: 700; letter-spacing: -0.03em; margin: 0; }
    .jp-sub { font-size: var(--fs-sm); margin: var(--sp-1) 0 0; }

    .jp-error {
      display: flex; align-items: center; justify-content: space-between; gap: var(--sp-3);
      padding: var(--sp-3) var(--sp-4);
      background: rgba(255, 64, 87, 0.08);
      border: 1px solid rgba(255, 64, 87, 0.35);
      border-radius: var(--radius-md);
      color: var(--red);
      font-size: var(--fs-sm);
    }
    .jp-error-dismiss {
      background: transparent; border: 0; color: var(--red);
      font-size: 1.2rem; cursor: pointer; padding: 0 var(--sp-2);
    }

    .jp-saved {
      padding: var(--sp-3) var(--sp-4);
      background: rgba(47, 219, 120, 0.10);
      border: 1px solid rgba(47, 219, 120, 0.35);
      border-radius: var(--radius-md);
      color: var(--green);
      font-size: var(--fs-sm);
      font-weight: 600;
    }

    .jp-loading {
      display: flex; align-items: center; gap: var(--sp-3);
      padding: var(--sp-3) 0; font-size: var(--fs-sm);
    }
    .jp-spinner {
      width: 16px; height: 16px;
      border: 2px solid var(--border);
      border-top-color: var(--green);
      border-radius: 50%;
      animation: jp-spin 0.8s linear infinite;
    }
    @keyframes jp-spin { to { transform: rotate(360deg); } }

    .jp-empty {
      padding: var(--sp-4);
      background: var(--bg-card-soft);
      border: 1px dashed var(--border);
      border-radius: var(--radius-md);
      color: var(--text-secondary);
      font-size: var(--fs-base);
      text-align: center;
    }

    .jp-form { display: flex; flex-direction: column; gap: var(--sp-5); }

    .jp-section { display: flex; flex-direction: column; gap: var(--sp-3); }
    .jp-section-title { font-size: var(--fs-sm); font-weight: 600; margin: 0; color: var(--text-secondary); }

    .jp-pills { display: flex; flex-wrap: wrap; gap: var(--sp-2); }
    .jp-pill {
      flex: 1 0 auto;
      min-height: 44px; /* WCAG / iOS HIG */
      padding: var(--sp-2) var(--sp-4);
      background: var(--bg-card-soft);
      border: 1px solid var(--border);
      border-radius: var(--radius-sm);
      color: var(--text-secondary);
      font: inherit; font-size: var(--fs-sm); font-weight: 500;
      cursor: pointer;
      transition: all 150ms ease;
    }
    .jp-pill:hover { border-color: var(--border-active); color: var(--text-main); }
    .jp-pill--active {
      background: var(--green-soft);
      border-color: var(--green);
      color: var(--green);
    }

    .jp-area {
      min-height: 110px;
      resize: vertical;
      line-height: 1.5;
    }

    .jp-counter {
      font-size: var(--fs-xs);
      color: var(--text-muted);
      align-self: flex-end;
      font-family: var(--font-mono);
    }
    .jcs-counter--over { color: var(--red); font-weight: 600; }

    .jp-tags-row { display: flex; gap: var(--sp-2); }
    .jp-tag-input { flex: 1; }

    .jp-chips {
      list-style: none; padding: 0; margin: 0;
      display: flex; flex-wrap: wrap; gap: var(--sp-2);
    }
    .jp-chip {
      padding: var(--sp-1) var(--sp-3);
      background: var(--green-soft);
      color: var(--green);
      border: 1px solid rgba(47, 219, 120, 0.35);
      border-radius: 9999px;
      font: inherit; font-size: var(--fs-xs); font-weight: 600;
      cursor: pointer;
      transition: background 150ms;
    }
    .jp-chip:hover { background: rgba(47, 219, 120, 0.20); }

    .jp-actions {
      display: flex; flex-wrap: wrap; gap: var(--sp-3);
      padding-top: var(--sp-3);
      border-top: 1px solid var(--border-soft);
    }
    .jp-save { min-width: 160px; }
    .jp-delete {
      border-color: rgba(255, 64, 87, 0.35);
      color: var(--red);
    }
    .jp-delete:hover:not(:disabled) {
      background: rgba(255, 64, 87, 0.10);
      border-color: var(--red);
    }
  `],
})
export class JournalPage {
  readonly state = inject(JournalState);

  readonly MOOD_LABELS = MOOD_LABELS;
  readonly MOODS: Mood[] = [1, 2, 3, 4, 5];
  readonly PLAN_MAX = PLAN_MAX;
  readonly REFLECTION_MAX = REFLECTION_MAX;
  readonly TAG_LEN_MAX = TAG_LEN_MAX;
  readonly TAGS_MAX = TAGS_MAX;

  // ===== Form state =====
  readonly moodPre = signal<Mood | null>(null);
  readonly moodDuring = signal<Mood | null>(null);
  readonly moodPost = signal<Mood | null>(null);
  readonly premarketPlan = signal('');
  readonly postmarketReflection = signal('');
  readonly tags = signal<string[]>([]);
  readonly tagDraft = signal('');

  /** Submit gate: backend is the source of truth for content validation.
   *  We only block structural issues (already saving) so the trader can submit
   *  an over-cap draft and let the backend respond with the precise message.
   *  Client-side caps are surfaced via the textarea `maxlength` + the visible counter. */
  readonly canSave = computed(() => !this.state.isSaving());

  readonly canAddTag = computed(() => {
    const draft = this.tagDraft().trim();
    return (
      draft.length > 0 &&
      draft.length <= TAG_LEN_MAX &&
      this.tags().length < TAGS_MAX &&
      !this.tags().includes(draft)
    );
  });

  readonly headerDate = computed(() => {
    const e = this.state.entry();
    return e?.localDate ?? new Date().toISOString().slice(0, 10);
  });

  readonly headerTz = computed(() => {
    const e = this.state.entry();
    return e?.timezone ?? Intl.DateTimeFormat().resolvedOptions().timeZone;
  });

  constructor() {
    // React to entry changes (post-save / post-delete re-hydrate the form).
    // We hydrate explicitly on load/save/remove to keep test-time behavior
    // deterministic; this effect is the safety net for any out-of-band
    // entry mutations (e.g. cross-tab refresh).
    effect(() => {
      const e = this.state.entry();
      this.hydrateFromEntry(e);
    });
  }

  // ===== Lifecycle =====
  ngOnInit(): void {
    void this.state.loadToday().then(() => this.hydrateFromEntry(this.state.entry()));
  }

  /** Mirror the DTO into the local form signals. Idempotent — safe to call multiple times. */
  private hydrateFromEntry(e: JournalEntryDto | null): void {
    this.moodPre.set(e?.moodPre ?? null);
    this.moodDuring.set(e?.moodDuring ?? null);
    this.moodPost.set(e?.moodPost ?? null);
    this.premarketPlan.set(e?.premarketPlan ?? '');
    this.postmarketReflection.set(e?.postmarketReflection ?? '');
    this.tags.set([...(e?.tags ?? [])]);
  }

  // ===== Form handlers =====
  toggleMood(slot: 'pre' | 'during' | 'post', value: Mood): void {
    const target =
      slot === 'pre' ? this.moodPre : slot === 'during' ? this.moodDuring : this.moodPost;
    target.set(target() === value ? null : value);
  }

  onPlanChange(v: string): void {
    this.premarketPlan.set(v.slice(0, PLAN_MAX));
  }

  onReflectionChange(v: string): void {
    this.postmarketReflection.set(v.slice(0, REFLECTION_MAX));
  }

  onTagDraftChange(v: string): void {
    this.tagDraft.set(v.slice(0, TAG_LEN_MAX));
  }

  addTag(): void {
    if (!this.canAddTag()) return;
    const next = this.tagDraft().trim();
    this.tags.update((list) => [...list, next]);
    this.tagDraft.set('');
  }

  removeTag(index: number): void {
    this.tags.update((list) => list.filter((_, i) => i !== index));
  }

  async onSave(): Promise<void> {
    if (!this.canSave() || this.state.isSaving()) return;
    const body: UpsertJournalEntryRequest = {
      moodPre: this.moodPre(),
      moodDuring: this.moodDuring(),
      moodPost: this.moodPost(),
      premarketPlan: this.premarketPlan().trim() || null,
      postmarketReflection: this.postmarketReflection().trim() || null,
      tags: this.tags(),
    };
    const saved = await this.state.save(body);
    if (saved) this.hydrateFromEntry(saved);
  }

  async onDelete(): Promise<void> {
    if (!this.state.hasEntry()) return;
    if (typeof window !== 'undefined' && !window.confirm('¿Borrar la entrada de hoy? Esta acción no se puede deshacer.')) {
      return;
    }
    await this.state.remove();
    this.hydrateFromEntry(null);
  }

  /** Formats `Date` as `HH:mm` for the "Guardado" banner. */
  savedLabel(ts: Date): string {
    const hh = String(ts.getHours()).padStart(2, '0');
    const mm = String(ts.getMinutes()).padStart(2, '0');
    return `${hh}:${mm}`;
  }
}
