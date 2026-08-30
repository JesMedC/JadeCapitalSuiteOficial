import { computed, inject, Injectable, signal } from '@angular/core';
import { AttachmentsService } from '../api/attachments.service';
import { AttachmentUsageDto } from '../api/attachments.types';

@Injectable({ providedIn: 'root' })
export class AttachmentQuotaState {
  private readonly svc = inject(AttachmentsService);

  readonly usage = signal<AttachmentUsageDto | null>(null);
  readonly isLoading = signal(false);
  readonly error = signal<string | null>(null);

  readonly quotaBytes = computed(() => this.usage()?.quotaBytes ?? 0);
  readonly usedBytes = computed(() => this.usage()?.totalBytes ?? 0);
  readonly remainingBytes = computed(() => Math.max(0, this.quotaBytes() - this.usedBytes()));
  readonly percentFull = computed(() => this.usage()?.percentFull ?? 0);
  readonly attachmentCount = computed(() => this.usage()?.attachmentCount ?? 0);
  readonly quotaCount = computed(() => this.usage()?.quotaCount ?? 0);

  async refresh(): Promise<void> {
    this.isLoading.set(true);
    this.error.set(null);
    try {
      const dto = await this.svc.getUsage();
      this.usage.set(dto);
    } catch (err: any) {
      this.error.set(this.formatError(err));
    } finally {
      this.isLoading.set(false);
    }
  }

  clear(): void {
    this.usage.set(null);
    this.error.set(null);
  }

  private formatError(err: any): string {
    if (err?.error?.detail) return String(err.error.detail);
    if (err?.error?.title) return String(err.error.title);
    if (err?.message) return String(err.message);
    return 'No pudimos cargar el uso de adjuntos.';
  }
}