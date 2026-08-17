import { ChangeDetectionStrategy, Component, computed, inject, input } from '@angular/core';
import { NgClass } from '@angular/common';
import { Router } from '@angular/router';
import { CoachingPromptDto, Severity, SEVERITY_ORDER } from './api/coaching.types';

// ============================================================================
//  CoachingPromptsComponent — slice 2d.2 frontend.
//
//  Standalone, reusable card list of coaching prompts. Designed to be
//  embedded in the trader dashboard (slice 2e) AND in the journal page
//  in Wave 3 if we want a contextual second surface.
//
//  Inputs (signal-based, Angular 18+):
//   - prompts: list of CoachingPromptDto (defaults to [])
//
//  Behavior:
//   - Empty list → renders "Sin prompts activos para este período".
//   - Non-empty → renders one card per prompt sorted by severity
//     (high first → low last). The backend already returns them in this
//     order, but we re-sort defensively in case the wire order ever
//     diverges from the contract.
//   - Each card has a CTA button that calls Router.navigateByUrl with
//     the prompt's cta.route (the CTA label is displayed verbatim).
//
//  Severity color tokens (per project convention):
//   - high    → --red
//   - medium  → --yellow
//   - low     → --blue
// ============================================================================

@Component({
  selector: 'jcs-coaching-prompts',
  standalone: true,
  imports: [NgClass],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <section class="coaching" aria-label="Coaching prompts">
      <header class="coaching-head">
        <h2>Coaching</h2>
        <p class="jcs-muted coaching-sub">Nudges basados en tu actividad reciente</p>
      </header>

      @if (sorted().length === 0) {
        <div class="coaching-empty jcs-card" data-testid="coaching-empty">
          <p class="jcs-muted">Sin prompts activos para este período.</p>
          <p class="jcs-muted coaching-empty-sub">Seguí operando y volvemos a chequear.</p>
        </div>
      } @else {
        <ul class="coaching-list" data-testid="coaching-list">
          @for (prompt of sorted(); track prompt.ruleId + prompt.occurredAt) {
            <li
              class="coaching-card jcs-card"
              [ngClass]="severityClass(prompt.severity)"
              [attr.data-testid]="'coaching-card-' + prompt.ruleId"
            >
              <header class="card-head">
                <span class="card-icon" [ngClass]="severityClass(prompt.severity)" aria-hidden="true">
                  @switch (prompt.severity) {
                    @case ('high') {
                      <svg viewBox="0 0 24 24" width="20" height="20" fill="none" stroke="currentColor" stroke-width="2.4" stroke-linecap="round" stroke-linejoin="round">
                        <path d="M12 9v4"/>
                        <path d="M12 17h.01"/>
                        <path d="M10.29 3.86 1.82 18a2 2 0 0 0 1.71 3h16.94a2 2 0 0 0 1.71-3L13.71 3.86a2 2 0 0 0-3.42 0z"/>
                      </svg>
                    }
                    @case ('medium') {
                      <svg viewBox="0 0 24 24" width="20" height="20" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
                        <circle cx="12" cy="12" r="10"/>
                        <path d="M12 8v4"/>
                        <path d="M12 16h.01"/>
                      </svg>
                    }
                    @default {
                      <svg viewBox="0 0 24 24" width="20" height="20" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
                        <circle cx="12" cy="12" r="10"/>
                        <path d="M9.09 9a3 3 0 0 1 5.83 1c0 2-3 3-3 3"/>
                        <path d="M12 17h.01"/>
                      </svg>
                    }
                  }
                </span>
                <div class="card-title">
                  <h3>{{ prompt.title }}</h3>
                  <span class="rule-badge" [attr.data-rule-id]="prompt.ruleId">{{ prompt.ruleId }}</span>
                </div>
              </header>
              <p class="card-body">{{ prompt.body }}</p>
              <button
                type="button"
                class="card-cta"
                (click)="onCta(prompt)"
                [attr.data-testid]="'coaching-cta-' + prompt.ruleId"
              >
                {{ prompt.cta.label }}
                <svg viewBox="0 0 24 24" width="14" height="14" fill="none" stroke="currentColor" stroke-width="2.4" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true">
                  <path d="M5 12h14"/>
                  <path d="m12 5 7 7-7 7"/>
                </svg>
              </button>
            </li>
          }
        </ul>
      }
    </section>
  `,
  styles: [`
    :host { display: block; }

    .coaching {
      display: flex;
      flex-direction: column;
      gap: var(--sp-3);
    }

    .coaching-head h2 {
      font-size: var(--fs-lg);
      margin: 0 0 var(--sp-1);
    }
    .coaching-head .coaching-sub {
      margin: 0;
      font-size: var(--fs-sm);
    }

    .coaching-list {
      display: flex;
      flex-direction: column;
      gap: var(--sp-3);
      list-style: none;
      padding: 0;
      margin: 0;
    }

    .coaching-card {
      padding: var(--sp-4) var(--sp-5);
      border-left-width: 4px;
      border-left-style: solid;
      display: flex;
      flex-direction: column;
      gap: var(--sp-3);
    }
    .coaching-card.sev-high {
      border-left-color: var(--red);
      box-shadow: 0 0 0 1px rgba(255, 64, 87, 0.18);
    }
    .coaching-card.sev-medium {
      border-left-color: var(--yellow);
      box-shadow: 0 0 0 1px rgba(255, 200, 64, 0.18);
    }
    .coaching-card.sev-low {
      border-left-color: var(--blue);
      box-shadow: 0 0 0 1px rgba(64, 169, 255, 0.16);
    }

    .card-head {
      display: flex;
      align-items: flex-start;
      gap: var(--sp-3);
    }
    .card-icon {
      width: 36px; height: 36px;
      border-radius: var(--radius-sm);
      display: inline-flex;
      align-items: center;
      justify-content: center;
      flex-shrink: 0;
    }
    .card-icon.sev-high {
      background: rgba(255, 64, 87, 0.12);
      color: var(--red);
    }
    .card-icon.sev-medium {
      background: rgba(255, 200, 64, 0.15);
      color: var(--yellow);
    }
    .card-icon.sev-low {
      background: rgba(64, 169, 255, 0.15);
      color: var(--blue);
    }
    .card-title {
      display: flex;
      flex-direction: column;
      gap: 4px;
      flex: 1;
    }
    .card-title h3 {
      margin: 0;
      font-size: var(--fs-md);
      font-weight: 600;
      letter-spacing: -0.01em;
    }
    .rule-badge {
      display: inline-flex;
      align-self: flex-start;
      font-family: var(--font-mono);
      font-size: var(--fs-xs);
      padding: 2px var(--sp-2);
      border-radius: 999px;
      background: var(--bg-card-soft);
      color: var(--text-muted);
      letter-spacing: 0.04em;
    }

    .card-body {
      margin: 0;
      font-size: var(--fs-sm);
      line-height: 1.5;
      color: var(--text-main);
    }

    .card-cta {
      align-self: flex-start;
      display: inline-flex;
      align-items: center;
      gap: var(--sp-2);
      padding: var(--sp-2) var(--sp-4);
      border-radius: var(--radius-sm);
      border: 1px solid var(--border-active);
      background: transparent;
      color: var(--text-main);
      font: inherit;
      font-size: var(--fs-sm);
      font-weight: 600;
      cursor: pointer;
      transition: background 150ms ease, transform 150ms ease;
    }
    .card-cta:hover {
      background: var(--bg-hover);
      transform: translateX(2px);
    }
    .card-cta:active {
      transform: translateX(0);
    }

    .coaching-empty {
      padding: var(--sp-5);
      display: flex;
      flex-direction: column;
      gap: var(--sp-1);
      align-items: flex-start;
    }
    .coaching-empty-sub { font-size: var(--fs-sm); margin: 0; }
  `],
})
export class CoachingPromptsComponent {
  private readonly router = inject(Router);

  readonly prompts = input<readonly CoachingPromptDto[]>([]);

  readonly sorted = computed(() => {
    // Server should already return high→low; we re-sort defensively
    // (cheap O(n log n)) so a future API change never silently breaks UI.
    return [...this.prompts()].sort((a, b) => {
      const cmp = SEVERITY_ORDER[a.severity] - SEVERITY_ORDER[b.severity];
      if (cmp !== 0) return cmp;
      // Newer first within a severity.
      return b.occurredAt.localeCompare(a.occurredAt);
    });
  });

  severityClass(s: Severity): string {
    return `sev-${s}`;
  }

  onCta(prompt: CoachingPromptDto): void {
    this.router.navigateByUrl(prompt.cta.route);
  }
}
