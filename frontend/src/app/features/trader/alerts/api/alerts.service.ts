import { Injectable, inject } from '@angular/core';
import { HttpClient, HttpErrorResponse, HttpParams } from '@angular/common/http';
import { firstValueFrom } from 'rxjs';
import { AlertDto } from './alerts.types';

// ============================================================================
//  AlertsService — slice 3b frontend.
//
//  Wraps the 3 alert endpoints:
//   - GET   /api/alerts?activeOnly=true|false  → AlertDto[]
//   - GET   /api/alerts/{id}                  → AlertDto | 404
//   - PATCH /api/alerts/{id}/ack              → AlertDto (idempotent)
//
//  Auth: auth.interceptor adds Bearer; we never set it here.
// ============================================================================

@Injectable({ providedIn: 'root' })
export class AlertsService {
  private readonly http = inject(HttpClient);
  private readonly base = '/api/alerts';

  async list(activeOnly: boolean = false): Promise<AlertDto[]> {
    const params = new HttpParams().set('activeOnly', String(activeOnly));
    return firstValueFrom(this.http.get<AlertDto[]>(this.base, { params }));
  }

  async getById(id: string): Promise<AlertDto> {
    return firstValueFrom(this.http.get<AlertDto>(`${this.base}/${id}`));
  }

  async acknowledge(id: string): Promise<AlertDto> {
    return firstValueFrom(this.http.patch<AlertDto>(`${this.base}/${id}/ack`, {}));
  }
}