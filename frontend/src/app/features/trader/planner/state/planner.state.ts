import { computed, inject, Injectable, signal } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { firstValueFrom } from 'rxjs';
import { PlannerWeekDto, PlannerSessionDto, PlannerStatus, UpsertPlannerSessionRequest } from '../api/planner.types';

@Injectable({ providedIn: 'root' })
export class PlannerState {
  private readonly http = inject(HttpClient);
  private readonly base = '/api/planner';

  async getWeek(weekStart: string): Promise<PlannerWeekDto> {
    return firstValueFrom(
      this.http.get<PlannerWeekDto>(`${this.base}/week`, { params: { week: weekStart } }),
    );
  }

  async createSession(body: UpsertPlannerSessionRequest): Promise<PlannerSessionDto> {
    return firstValueFrom(this.http.post<PlannerSessionDto>(`${this.base}/sessions`, body));
  }

  async updateStatus(id: string, newStatus: PlannerStatus): Promise<PlannerSessionDto> {
    return firstValueFrom(
      this.http.patch<PlannerSessionDto>(`${this.base}/sessions/${id}/status`, { newStatus }),
    );
  }

  readonly weekStart = signal<string>('');
  readonly week = signal<PlannerWeekDto | null>(null);
  readonly isLoading = signal(false);
  readonly isSaving = signal(false);
  readonly error = signal<string | null>(null);

  readonly hasWeek = computed(() => this.week() !== null);
  readonly sessions = computed(() => this.week()?.sessions ?? []);
  readonly comparison = computed(() => this.week()?.comparison ?? null);

  async loadWeek(weekStart: string): Promise<void> {
    this.isLoading.set(true);
    this.error.set(null);
    this.weekStart.set(weekStart);
    try {
      const week = await this.getWeek(weekStart);
      this.week.set(week);
    } catch (err: any) {
      this.error.set(this.formatError(err));
      this.week.set(null);
    } finally {
      this.isLoading.set(false);
    }
  }

  async createSessionSignal(body: UpsertPlannerSessionRequest): Promise<PlannerSessionDto | null> {
    this.isSaving.set(true);
    this.error.set(null);
    try {
      const saved = await this.createSession(body);
      await this.loadWeek(this.weekStart());
      return saved;
    } catch (err: any) {
      this.error.set(this.formatError(err));
      return null;
    } finally {
      this.isSaving.set(false);
    }
  }

  async changeStatus(id: string, newStatus: PlannerStatus): Promise<boolean> {
    this.isSaving.set(true);
    this.error.set(null);
    try {
      await this.updateStatus(id, newStatus);
      await this.loadWeek(this.weekStart());
      return true;
    } catch (err: any) {
      this.error.set(this.formatError(err));
      return false;
    } finally {
      this.isSaving.set(false);
    }
  }

  clearError(): void {
    this.error.set(null);
  }

  private formatError(err: any): string {
    if (err?.error?.detail) return String(err.error.detail);
    if (err?.error?.title) return String(err.error.title);
    if (err?.message) return String(err.message);
    return 'No pudimos conectar con el servidor.';
  }
}
