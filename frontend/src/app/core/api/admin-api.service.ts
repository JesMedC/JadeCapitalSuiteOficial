import { Injectable, inject } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { firstValueFrom } from 'rxjs';

// ============================================================================
//  Admin.Subscriptions — slice 0f endpoints wrapped for the frontend.
//  All endpoints require AdminOnly policy; the admin.guard.ts enforces it
//  at the route level, so by the time these methods run the caller is Admin.
// ============================================================================

export type SubscriptionStatus =
  | 'Pending' | 'Active' | 'Trial' | 'PastDue' | 'Suspended' | 'Cancelled';

export interface PagedSubscriptions {
  total: number;
  page: number;
  pageSize: number;
  items: SubscriptionSummary[];
}

export interface SubscriptionSummary {
  id: string;
  ownerUserId: string;
  ownerEmail: string;
  ownerDisplayName: string;
  planCode: string;
  status: SubscriptionStatus;
  trialEndsAt: string | null;
  currentPeriodEnd: string;
  version: number;
  createdAt: string;
  updatedAt: string;
}

export interface PlanInfo {
  code: string;
  name: string;
  priceCents: number;
  currency: string;
  intervalDays: number;
}

export interface HistoryEntry {
  id: string;
  occurredAt: string;
  actor: string;
  kind: string;
  fromStatus: SubscriptionStatus | null;
  toStatus: SubscriptionStatus | null;
  fromPlanCode: string | null;
  toPlanCode: string | null;
  reason: string | null;
  notes: string | null;
}

export interface SubscriptionDetail {
  id: string;
  ownerUserId: string;
  ownerEmail: string;
  ownerDisplayName: string;
  plan: PlanInfo;
  status: SubscriptionStatus;
  trialEndsAt: string | null;
  currentPeriodStart: string;
  currentPeriodEnd: string;
  version: number;
  history: HistoryEntry[];
  createdAt: string;
  updatedAt: string | null;
}

@Injectable({ providedIn: 'root' })
export class AdminApiService {
  private readonly http = inject(HttpClient);

  async list(status: SubscriptionStatus | '', page = 1, pageSize = 20): Promise<PagedSubscriptions> {
    let params = new HttpParams().set('page', page).set('pageSize', pageSize);
    if (status) params = params.set('status', status);
    return firstValueFrom(
      this.http.get<PagedSubscriptions>('/api/admin/subscriptions', { params }),
    );
  }

  async detail(id: string): Promise<SubscriptionDetail> {
    return firstValueFrom(
      this.http.get<SubscriptionDetail>(`/api/admin/subscriptions/${id}`),
    );
  }

  async changeTier(id: string, newPlanCode: string, observedVersion: number): Promise<void> {
    await firstValueFrom(
      this.http.post<void>(`/api/admin/subscriptions/${id}/change-tier`, {
        newPlanCode,
        observedVersion,
      }),
    );
  }

  async cancel(id: string, reason: string, observedVersion: number): Promise<void> {
    await firstValueFrom(
      this.http.post<void>(`/api/admin/subscriptions/${id}/cancel`, {
        reason,
        observedVersion,
      }),
    );
  }

  async extendTrial(id: string, newTrialEndsAt: string, observedVersion: number): Promise<void> {
    await firstValueFrom(
      this.http.post<void>(`/api/admin/subscriptions/${id}/extend-trial`, {
        newTrialEndsAt,
        observedVersion,
      }),
    );
  }
}
