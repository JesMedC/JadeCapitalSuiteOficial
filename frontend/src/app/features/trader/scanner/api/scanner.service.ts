import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { RunScannerRequest, ScannerFilterDto, ScanResultDto, UpsertScannerFilterRequest } from './scanner.types';

@Injectable({ providedIn: 'root' })
export class ScannerService {
  private readonly http = inject(HttpClient);
  private readonly base = '/api/scanner';

  async listFilters(): Promise<ScannerFilterDto[]> {
    return firstValueFrom(this.http.get<ScannerFilterDto[]>(`${this.base}/filters`));
  }

  async createFilter(body: UpsertScannerFilterRequest): Promise<ScannerFilterDto> {
    return firstValueFrom(this.http.post<ScannerFilterDto>(`${this.base}/filters`, body));
  }

  async updateFilter(id: string, body: UpsertScannerFilterRequest): Promise<ScannerFilterDto> {
    return firstValueFrom(this.http.patch<ScannerFilterDto>(`${this.base}/filters/${id}`, body));
  }

  async deleteFilter(id: string): Promise<void> {
    await firstValueFrom(this.http.delete<void>(`${this.base}/filters/${id}`));
  }

  async run(body: RunScannerRequest): Promise<ScanResultDto[]> {
    return firstValueFrom(this.http.post<ScanResultDto[]>(`${this.base}/run`, body));
  }
}
