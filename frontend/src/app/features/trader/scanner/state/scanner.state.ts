import { computed, inject, Injectable, signal } from '@angular/core';
import { RunScannerRequest, ScannerFilterDto, ScanResultDto, UpsertScannerFilterRequest } from '../api/scanner.types';
import { ScannerService } from '../api/scanner.service';

@Injectable({ providedIn: 'root' })
export class ScannerState {
  private readonly svc = inject(ScannerService);

  readonly filters = signal<ScannerFilterDto[]>([]);
  readonly results = signal<ScanResultDto[]>([]);
  readonly isLoading = signal(false);
  readonly isSaving = signal(false);
  readonly error = signal<string | null>(null);

  readonly hasFilters = computed(() => this.filters().length > 0);

  async loadFilters(): Promise<void> {
    this.isLoading.set(true);
    this.error.set(null);
    try {
      const list = await this.svc.listFilters();
      this.filters.set(list);
    } catch (err: any) {
      this.error.set(this.formatError(err));
    } finally {
      this.isLoading.set(false);
    }
  }

  async saveFilter(body: UpsertScannerFilterRequest, id?: string): Promise<boolean> {
    this.isSaving.set(true);
    this.error.set(null);
    try {
      if (id) await this.svc.updateFilter(id, body);
      else await this.svc.createFilter(body);
      await this.loadFilters();
      return true;
    } catch (err: any) {
      this.error.set(this.formatError(err));
      return false;
    } finally {
      this.isSaving.set(false);
    }
  }

  async deleteFilter(id: string): Promise<boolean> {
    this.error.set(null);
    try {
      await this.svc.deleteFilter(id);
      await this.loadFilters();
      return true;
    } catch (err: any) {
      this.error.set(this.formatError(err));
      return false;
    }
  }

  async run(req: RunScannerRequest): Promise<void> {
    this.isLoading.set(true);
    this.error.set(null);
    try {
      const list = await this.svc.run(req);
      this.results.set(list);
    } catch (err: any) {
      this.error.set(this.formatError(err));
      this.results.set([]);
    } finally {
      this.isLoading.set(false);
    }
  }

  clearError(): void { this.error.set(null); }

  private formatError(err: any): string {
    if (err?.error?.detail) return String(err.error.detail);
    if (err?.error?.title) return String(err.error.title);
    if (err?.message) return String(err.message);
    return 'No pudimos conectar con el servidor.';
  }
}
