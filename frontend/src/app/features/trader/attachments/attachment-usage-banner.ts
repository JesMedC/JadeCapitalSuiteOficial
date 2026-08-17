import { ChangeDetectionStrategy, Component, OnDestroy, OnInit, computed, effect, inject } from '@angular/core';
import { AttachmentQuotaState } from './state/attachment-quota.state';

/**
 * Compact storage-usage banner for the trader shell. Slice 4d (Wave 4).
 *
 * Shows current usage / quota + a color-coded progress bar. Polls every
 * 60s so the indicator stays fresh without forcing a refresh on the
 * user. Mounted in the trader-shell's sidebar (top-right) so it's
 * visible across every page.
 */
@Component({
  selector: 'jcs-attachment-usage-banner',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @if (state.usage(); as usage) {
      <div class="banner" role="status" aria-label="Almacenamiento de adjuntos">
        <div class="row">
          <span class="label">Adjuntos</span>
          <span class="value">{{ usedMb() }} / {{ quotaMb() }} MB</span>
        </div>
        <div class="bar" [attr.aria-valuenow]="state.percentFull()" aria-valuemin="0" aria-valuemax="100">
          <div class="fill" [class.warn]="warn()" [class.danger]="danger()" [style.width.%]="state.percentFull()"></div>
        </div>
        <div class="row">
          <span class="count">{{ state.attachmentCount() }} / {{ state.quotaCount() }} archivos</span>
          <span class="pct">{{ state.percentFull() }}%</span>
        </div>
      </div>
    } @else if (state.error() !== null) {
      <div class="banner error" role="alert">{{ state.error() }}</div>
    }
  `,
  styles: [`
    :host { display: block; }

    .banner {
      padding: var(--sp-3);
      background: var(--bg-card-soft);
      border: 1px solid var(--border-soft);
      border-radius: var(--radius-md);
      font-size: 0.7rem;
      color: var(--text-muted);
      display: flex;
      flex-direction: column;
      gap: var(--sp-1);
    }
    .banner.error {
      border-color: rgba(255, 64, 87, 0.4);
      color: var(--red);
    }

    .row {
      display: flex;
      justify-content: space-between;
      gap: var(--sp-2);
    }

    .label { font-weight: 600; text-transform: uppercase; letter-spacing: 0.06em; color: var(--text-soft); }
    .value { color: var(--text-main); font-weight: 600; font-variant-numeric: tabular-nums; }
    .count { color: var(--text-soft); font-variant-numeric: tabular-nums; }
    .pct { color: var(--text-soft); font-variant-numeric: tabular-nums; font-weight: 600; }

    .bar {
      position: relative;
      width: 100%;
      height: 4px;
      background: var(--bg-hover);
      border-radius: 2px;
      overflow: hidden;
    }
    .fill {
      position: absolute;
      top: 0;
      left: 0;
      height: 100%;
      background: var(--green);
      transition: width 200ms ease, background 150ms ease;
    }
    .fill.warn { background: #f0b132; }
    .fill.danger { background: var(--red); }
  `],
})
export class AttachmentUsageBanner implements OnInit, OnDestroy {
  readonly state = inject(AttachmentQuotaState);

  private pollHandle?: number;

  usedMb = computed(() => this.formatMb(this.state.usedBytes()));
  quotaMb = computed(() => this.formatMb(this.state.quotaBytes()));
  warn = computed(() => this.state.percentFull() >= 70 && this.state.percentFull() < 90);
  danger = computed(() => this.state.percentFull() >= 90);

  constructor() {
    // No-op effect — placeholder for future "react to usage changes" wiring.
    effect(() => {
      if (this.state.usage()) {
        // Trigger refresh on usage change is a future hook; no-op for now.
      }
    });
  }

  ngOnInit(): void {
    void this.state.refresh();
    if (typeof window !== 'undefined') {
      this.pollHandle = window.setInterval(() => void this.state.refresh(), 60_000);
    }
  }

  ngOnDestroy(): void {
    if (this.pollHandle !== undefined && typeof window !== 'undefined') {
      window.clearInterval(this.pollHandle);
    }
  }

  private formatMb(bytes: number): string {
    if (bytes <= 0) return '0';
    const mb = bytes / (1024 * 1024);
    return mb >= 100 ? mb.toFixed(0) : mb.toFixed(1);
  }
}