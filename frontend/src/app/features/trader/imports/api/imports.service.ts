import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { BeginImportResponse, ImportJobDto } from './imports.types';

@Injectable({ providedIn: 'root' })
export class ImportsService {
  private readonly http = inject(HttpClient);
  private readonly base = '/api/imports';

  /** Uploads a CSV file. Returns the new ImportJob id; status is polled via getStatus(). */
  async uploadCsv(file: File, accountId: string): Promise<BeginImportResponse> {
    const form = new FormData();
    form.append('file', file, file.name);
    form.append('accountId', accountId);
    return firstValueFrom(this.http.post<BeginImportResponse>(`${this.base}/csv`, form));
  }

  /** Polls the current status of an import job. */
  async getStatus(jobId: string): Promise<ImportJobDto> {
    return firstValueFrom(this.http.get<ImportJobDto>(`${this.base}/${jobId}`));
  }
}