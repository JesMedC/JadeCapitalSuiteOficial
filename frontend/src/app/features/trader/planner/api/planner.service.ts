import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { PlannerWeekDto, PlannerSessionDto, PlannerStatus, UpsertPlannerSessionRequest } from './planner.types';

@Injectable({ providedIn: 'root' })
export class PlannerService {
  private readonly http = inject(HttpClient);
  private readonly base = '/api/planner';

  async getWeek(weekStart: string): Promise<PlannerWeekDto> {
    return firstValueFrom(
      this.http.get<PlannerWeekDto>(`${this.base}/week`, { params: { week: weekStart } }),
    );
  }

  async upsert(body: UpsertPlannerSessionRequest): Promise<PlannerSessionDto> {
    return firstValueFrom(this.http.post<PlannerSessionDto>(`${this.base}/sessions`, body));
  }

  async update(id: string, body: UpsertPlannerSessionRequest): Promise<PlannerSessionDto> {
    return firstValueFrom(this.http.patch<PlannerSessionDto>(`${this.base}/sessions/${id}`, body));
  }

  async updateStatus(id: string, newStatus: PlannerStatus): Promise<PlannerSessionDto> {
    return firstValueFrom(
      this.http.patch<PlannerSessionDto>(`${this.base}/sessions/${id}/status`, { newStatus }),
    );
  }

  async getById(id: string): Promise<PlannerSessionDto | null> {
    try {
      return await firstValueFrom(this.http.get<PlannerSessionDto>(`${this.base}/sessions/${id}`));
    } catch (err: any) {
      if (err?.status === 404) return null;
      throw err;
    }
  }
}
