import { Injectable, inject } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { firstValueFrom } from 'rxjs';

// ============================================================================
//  Admin.Subscriptions — slice 0f endpoints wrapped for the frontend.
//  All endpoints require AdminOnly policy; admin.guard.ts enforces it at the
//  route level, so by the time these methods run the caller is Admin.
//
//  DTOs mirror the wire shape from
//  src/2.Modules/Billing/JadeCapital.Billing.Contracts/Subscriptions/SubscriptionDtos.cs
// ============================================================================

export interface PagedSubscriptions {
  total: number;
  page: number;
  pageSize: number;
  items: SubscriptionListItem[];
}

export interface SubscriptionListItem {
  subscriptionId: string;
  userId: string;
  planCode: string;
  status: string;
  updatedAt: string;
  version: number;
}

export interface SubscriptionDetail {
  subscriptionId: string;
  userId: string;
  planCode: string;
  planName: string;
  status: string;
  trialEndsAt: string | null;
  createdAt: string;
  updatedAt: string | null;
  version: number;
  owner: UserOwnerProjection;
  history: SubscriptionHistoryItem[];
}

export interface UserOwnerProjection {
  email: string;
  displayName: string;
}

export interface SubscriptionHistoryItem {
  id: string;
  action: string;
  priorPlanCode: string;
  resultingPlanCode: string;
  priorStatus: string;
  resultingStatus: string;
  actor: string;
  occurredAt: string;
  version: number;
  reason: string | null;
  priorTrialEndsAt: string | null;
  newTrialEndsAt: string | null;
}

@Injectable({ providedIn: 'root' })
export class AdminApiService {
  private readonly http = inject(HttpClient);

  async list(status: string | '', page = 1, pageSize = 20): Promise<PagedSubscriptions> {
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
