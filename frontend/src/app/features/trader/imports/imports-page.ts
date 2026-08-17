import { ChangeDetectionStrategy, Component, computed, inject, OnDestroy, signal } from '@angular/core';
import { DecimalPipe } from '@angular/common';
import { ImportsService } from './api/imports.service';
import { ImportsState } from './state/imports.state';
import { IMPORT_STATUS_LABELS } from './api/imports.types';

@Component({
  selector: 'jcs-imports-page',
  standalone: true,
  imports: [DecimalPipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <section class="imports-page">
      <header class="imports-header">
        <h1 class="imports-title">Importar historial de trades</h1>
        <p class="imports-subtitle jcs-muted">
          Subí tu CSV (formato <strong>Ticket, Symbol, Open Time, Type, Volume,
          Open Price, Close Price, Close Time, Commission, Swap, Profit</strong>).
          El parser detecta el formato y procesa en bloques de 50 filas.
        </p>
      </header>

      @if (state.error(); as err) {
        <div class="jcs-card jcs-card--soft planner-error" role="alert">
          <strong>Error:</strong> {{ err }}
          <button type="button" class="jcs-btn jcs-btn--ghost jcs-btn--sm" (click)="state.clearError()">Cerrar</button>
        </div>
      }

      <div class="drop-zone jcs-card"
           [class.drop-zone--hover]="dragOver()"
           (dragover)="$event.preventDefault(); dragOver.set(true)"
           (dragleave)="dragOver.set(false)"
           (drop)="onDrop($event)">
        @if (!selectedFile()) {
          <p class="drop-zone__hint">Arrastrá tu <code>.csv</code> acá, o</p>
          <button type="button" class="jcs-btn jcs-btn--primary" (click)="picker.click()">
            Elegir archivo
          </button>
          <input #picker type="file" accept=".csv,text/csv" hidden (change)="onFilePicked($event)" />
        } @else {
          <p class="drop-zone__file">
            <strong>{{ selectedFile()!.name }}</strong>
            <span class="jcs-muted"> · {{ selectedFile()!.size | number }} bytes</span>
          </p>
          <div class="drop-zone__actions">
            <input #accountIdInput type="text" placeholder="Account ID (UUID)"
                   [value]="accountId()"
                   (input)="accountId.set(asInputValue($event))" />
            <button type="button" class="jcs-btn jcs-btn--primary"
                    [disabled]="!canUpload() || state.isUploading()"
                    (click)="upload()">
              @if (state.isUploading()) { Subiendo... } @else { Importar }
            </button>
            <button type="button" class="jcs-btn jcs-btn--ghost" (click)="reset()">Cancelar</button>
          </div>
        }
      </div>

      @if (state.status() !== 'idle') {
        <div class="import-status jcs-card">
          <header class="import-status__head">
            <strong>Estado: {{ statusLabel() }}</strong>
            <span class="jcs-muted">{{ state.progressPercent() }}%</span>
          </header>
          <div class="progress-bar">
            <div class="progress-bar__fill" [style.width.%]="state.progressPercent()"></div>
          </div>
          <dl class="progress-grid">
            <div><dt>Importadas</dt><dd>{{ state.progress().imported }}</dd></div>
            <div><dt>Saltadas</dt><dd>{{ state.progress().skipped }}</dd></div>
            <div><dt>Con error</dt><dd>{{ state.progress().errored }}</dd></div>
            <div><dt>Total</dt><dd>{{ state.progress().total }}</dd></div>
          </dl>
          @if (state.isCompleted()) {
            <p class="import-status__done">Importación completa.</p>
          }
        </div>
      }
    </section>
  `,
  styles: [`
    :host { display: block; }
    .imports-page { display: flex; flex-direction: column; gap: var(--sp-5); }
    .imports-header h1 { font-size: var(--fs-2xl); margin: 0 0 var(--sp-2); }
    .imports-subtitle { font-size: var(--fs-sm); margin: 0; }
    .imports-subtitle code {
      background: var(--bg-card-soft); padding: 2px 6px; border-radius: var(--radius-sm);
      font-family: var(--font-mono, monospace); font-size: 0.8rem;
    }
    .drop-zone {
      display: flex; flex-direction: column; align-items: center; gap: var(--sp-3);
      padding: var(--sp-6); border: 2px dashed var(--border);
      transition: border-color 200ms, background 200ms;
    }
    .drop-zone--hover {
      border-color: var(--green); background: var(--green-soft);
    }
    .drop-zone__hint { margin: 0; font-size: var(--fs-sm); color: var(--text-muted); }
    .drop-zone__file { margin: 0; font-size: var(--fs-base); }
    .drop-zone__actions {
      display: flex; gap: var(--sp-2); align-items: center; flex-wrap: wrap;
    }
    .drop-zone__actions input {
      padding: var(--sp-2) var(--sp-3); border: 1px solid var(--border);
      border-radius: var(--radius-sm); font-family: var(--font-mono, monospace); font-size: 0.8rem;
      min-width: 320px;
    }
    .import-status { padding: var(--sp-5); }
    .import-status__head {
      display: flex; justify-content: space-between; align-items: baseline;
      margin-bottom: var(--sp-3);
    }
    .progress-bar {
      height: 6px; background: var(--bg-card-soft); border-radius: 3px;
      overflow: hidden; margin-bottom: var(--sp-4);
    }
    .progress-bar__fill {
      height: 100%; background: var(--green); transition: width 300ms;
    }
    .progress-grid {
      display: grid; grid-template-columns: repeat(4, 1fr); gap: var(--sp-4);
      margin: 0; padding: 0;
    }
    .progress-grid dt { font-size: 0.7rem; color: var(--text-muted); margin: 0; text-transform: uppercase; }
    .progress-grid dd { font-size: var(--fs-xl); margin: var(--sp-1) 0 0; font-weight: 700; }
    .import-status__done { color: var(--green); font-weight: 600; margin: var(--sp-3) 0 0; }
    @media (max-width: 600px) {
      .progress-grid { grid-template-columns: repeat(2, 1fr); }
      .drop-zone__actions input { min-width: 0; flex: 1; }
    }
  `],
})
export class ImportsPage implements OnDestroy {
  readonly state = inject(ImportsState);
  private readonly svc = inject(ImportsService);

  readonly selectedFile = signal<File | null>(null);
  readonly accountId = signal('');
  readonly dragOver = signal(false);

  private pollHandle: number | null = null;

  readonly canUpload = computed(() => {
    const f = this.selectedFile();
    if (!f) return false;
    const id = this.accountId().trim();
    if (!id) return false;
    // Accept either a valid GUID or a non-empty placeholder — endpoint validates.
    return f.size <= 10 * 1024 * 1024;
  });

  readonly statusLabel = computed(() => {
    const job = this.state.currentJob();
    if (!job) {
      if (this.state.isUploading()) return 'Subiendo';
      if (this.state.isPolling()) return 'Importando';
      return '—';
    }
    return IMPORT_STATUS_LABELS[job.status] ?? '—';
  });

  onFilePicked(event: Event): void {
    const input = event.target as HTMLInputElement;
    const file = input.files?.[0];
    if (file) this.selectedFile.set(file);
  }

  onDrop(event: DragEvent): void {
    event.preventDefault();
    this.dragOver.set(false);
    const file = event.dataTransfer?.files?.[0];
    if (file) this.selectedFile.set(file);
  }

  asInputValue(event: Event): string {
    return (event.target as HTMLInputElement).value;
  }

  async upload(): Promise<void> {
    const file = this.selectedFile();
    const accId = this.accountId().trim();
    if (!file || !accId) return;

    const jobId = await this.state.upload(file, accId);
    if (!jobId) return;

    // Start polling. 1s tick matches the spec; complete/failed/error transitions stop the loop.
    this.startPolling(jobId);
  }

  reset(): void {
    if (this.pollHandle !== null) {
      clearInterval(this.pollHandle);
      this.pollHandle = null;
    }
    this.selectedFile.set(null);
    this.accountId.set('');
    this.state.reset();
  }

  ngOnDestroy(): void {
    if (this.pollHandle !== null) {
      clearInterval(this.pollHandle);
      this.pollHandle = null;
    }
  }

  private startPolling(jobId: string): void {
    if (this.pollHandle !== null) clearInterval(this.pollHandle);
    this.pollHandle = window.setInterval(async () => {
      try {
        const job = await this.svc.getStatus(jobId);
        this.state.setJob(job);
        if (job.status === 2 || job.status === 3 || job.status === 4) {
          if (this.pollHandle !== null) {
            clearInterval(this.pollHandle);
            this.pollHandle = null;
          }
        }
      } catch (err: any) {
        // Stop polling on errors — the status endpoint will surface a 401 if
        // the session expired; we don't want to hammer the server.
        if (this.pollHandle !== null) {
          clearInterval(this.pollHandle);
          this.pollHandle = null;
        }
      }
    }, 1000);
  }
}