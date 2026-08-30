import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { firstValueFrom } from 'rxjs';
import { PlanInfo } from './plan-info';

// ============================================================================
//  PlanApiService — Wave-1.3
//
//  Wraps GET /api/billing/plans. The endpoint is AllowAnonymous on the
//  backend (BillingPublicEndpoints.MapBillingPublicEndpoints), so this
//  service does NOT require an auth token. Pricing/landing pages load the
//  catalog before login.
//
//  Marketing copy (the bullet lists under each plan) is intentionally NOT
//  in this response — it lives in FEATURES_BY_CODE on the consumer side and
//  is merged at render time. The backend only exposes canonical price +
//  name + currency; marketing wording stays a marketing concern.
// ============================================================================

@Injectable({ providedIn: 'root' })
export class PlanApiService {
  private readonly http = inject(HttpClient);

  async list(): Promise<PlanInfo[]> {
    return firstValueFrom(this.http.get<PlanInfo[]>('/api/billing/plans'));
  }
}
