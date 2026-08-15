namespace JadeCapital.Billing.PublicApi.Contracts;

/// <summary>
/// Public DTO for the plan catalog. Wave-1.3 — fed to the marketing pricing
/// pages on the frontend (no auth required). Mirrors the wire shape consumed
/// by <c>frontend/src/app/core/api/plan-api.service.ts</c>.
///
/// Money is decimal (no float/double ever) and carries its currency code so
/// the frontend can render a localised price prefix without losing
/// precision.
/// </summary>
public sealed record PlanInfo(
    string Code,
    string Name,
    decimal MonthlyPrice,
    string Currency,
    bool IsEligibleForSelfService);
