import { computed, inject, Injectable, signal } from '@angular/core';
import { ImportsService } from '../api/imports.service';
import { ImportJobDto } from '../api/imports.types';

@Injectable({ providedIn: 'root' })
export class ImportsState {
  private readonly svc = inject(ImportsService);

  readonly currentJob = signal<ImportJobDto | null>(null);
  readonly status = signal<'idle' | 'uploading' | 'polling' | 'completed' | 'failed'>('idle');
  readonly progress = signal({ imported: 0, skipped: 0, errored: 0, total: 0 });
  readonly error = signal<string | null>(null);

  readonly isUploading = computed(() => this.status() === 'uploading');
  readonly isPolling = computed(() => this.status() === 'polling');
  readonly isCompleted = computed(() => this.status() === 'completed');
  readonly isFailed = computed(() => this.status() === 'failed');

  /** Polling percentage (0..100). Falls back to 0 when total is unknown. */
  readonly progressPercent = computed(() => {
    const p = this.progress();
    if (p.total <= 0) return 0;
    return Math.min(100, Math.round((p.imported + p.skipped + p.errored) / p.total * 100));
  });

  async upload(file: File, accountId: string): Promise<string | null> {
    this.status.set('uploading');
    this.error.set(null);
    this.progress.set({ imported: 0, skipped: 0, errored: 0, total: 0 });
    this.currentJob.set(null);

    try {
      const res = await this.svc.uploadCsv(file, accountId);
      this.status.set('polling');
      return res.importJobId;
    } catch (err: any) {
      this.status.set('failed');
      this.error.set(this.formatError(err));
      return null;
    }
  }

  /** Updates the current job snapshot — typically called by the page's polling loop. */
  setJob(job: ImportJobDto): void {
    this.currentJob.set(job);
    this.progress.set({
      imported: job.rowsImported,
      skipped: job.rowsSkipped,
      errored: job.rowsErrored,
      total: job.rowsTotal,
    });

    if (job.status === 2) this.status.set('completed');
    else if (job.status === 3) {
      this.status.set('failed');
      this.error.set(job.errorMessage ?? 'Import failed.');
    } else if (job.status === 1) this.status.set('polling');
  }

  reset(): void {
    this.status.set('idle');
    this.currentJob.set(null);
    this.progress.set({ imported: 0, skipped: 0, errored: 0, total: 0 });
    this.error.set(null);
  }

  clearError(): void { this.error.set(null); }

  private formatError(err: any): string {
    if (err?.error?.detail) return String(err.error.detail);
    if (err?.error?.title) return String(err.error.title);
    if (err?.message) return String(err.message);
    return 'No pudimos conectar con el servidor.';
  }
}