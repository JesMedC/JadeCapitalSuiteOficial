// ============================================================================
//  Billing Portal API types — slice 6b.2 frontend.
//
//  Wire shape mirror of:
//    src/2.Modules/Billing/JadeCapital.Billing.Contracts/Portal/BillingPortalDtos.cs
//
//  ASP.NET Core minimal API returns the records with the default camelCase
//  policy (no global JsonNamingPolicy is configured in
//  src/1.Api/JadeCapital.Host/Program.cs — see Wave 6a.2/6b.1 DTOs for the
//  same precedent). The snake_case naming in the backend DTO doc comments is
//  documentation drift; the actual HTTP response is camelCase, matching the
//  rest of the project (PlanInfo, StrategyDto, StripeCustomerDto, etc.).
//
//  The portal DTOs are a strict subset of the upstream Stripe wire shapes —
//  only the fields the FE renders. Internal-only fields (Stripe metadata,
//  version tokens, raw status enums) stay server-side.
// ============================================================================

/**
 * Subscription summary for the billing portal.
 * Wire: `GET /api/billing/portal/subscription`.
 */
export interface BillingPortalSubscriptionDto {
  stripeSubscriptionId: string;
  status: string;
  planCode: string;
  currentPeriodEnd: string;
  cancelAtPeriodEnd: boolean;
}

/**
 * Saved payment method on the billing portal.
 * Wire: `GET /api/billing/portal/payment-methods`.
 */
export interface BillingPortalPaymentMethodDto {
  id: string;
  brand: string;
  last4: string;
  expiresAt: string | null;
  isDefault: boolean;
}

/**
 * Invoice on the billing portal.
 * Wire: `GET /api/billing/portal/invoices`.
 * `amountCents` is integer cents (mirrors Stripe's own shape — never decimal).
 * `paidAt` is null for unpaid invoices (status `open`, `draft`, etc.).
 */
export interface BillingPortalInvoiceDto {
  id: string;
  number: string;
  amountCents: number;
  currency: string;
  issuedAt: string;
  paidAt: string | null;
  status: string;
  pdfUrl: string;
}

/**
 * Stripe Customer Portal session.
 * Wire: `POST /api/billing/stripe/portal` → `StripePortalSessionDto`.
 * The FE redirects the user to `url`; Stripe hosts the self-service UI
 * (cancel subscription, update card, view invoices).
 */
export interface StripePortalSessionDto {
  sessionId: string;
  url: string;
  expiresAt: string;
}

/** RFC 7807 problem shape returned by the backend on 422 / 404 / 5xx. */
export interface BillingPortalProblem {
  type?: string;
  title?: string;
  status?: number;
  detail?: string;
  code?: string;
  errors?: Record<string, string[]>;
}
