using JadeCapital.Billing.Domain.Common;
using JadeCapital.Shared.Kernel.Money;
using JadeCapital.Shared.Kernel.Primitives;
using JadeCapital.Shared.Kernel.Results;

namespace JadeCapital.Billing.Domain.Subscriptions;

/// <summary>
/// Catalog entity representing a billable plan offered by Billing. Owns the
/// eligibility flag that decides whether the plan may be self-selected via
/// <c>Subscription.ChangeTier</c>; Admin-managed (out of scope for 0e).
/// </summary>
public sealed class Plan : Entity<Guid>
{
    public PlanCode Code { get; private set; } = default!;
    public string Name { get; private set; } = default!;
    public Money MonthlyPrice { get; private set; } = default!;

    /// <summary>
    /// When <c>true</c>, the plan is a self-service candidate for
    /// <c>Subscription.ChangeTier</c>. Inert plans (e.g. legacy SKUs) cannot
    /// be the target of a tier change.
    /// </summary>
    public bool IsEligibleForSelfService { get; private set; }

    /// <summary>Soft deprecation marker (read-only filter for Admin).</summary>
    public bool IsDeprecated { get; private set; }

    private Plan() { }

    private Plan(
        Guid id,
        PlanCode code,
        string name,
        Money monthlyPrice,
        bool isEligibleForSelfService,
        bool isDeprecated) : base(id)
    {
        Code = code;
        Name = name;
        MonthlyPrice = monthlyPrice;
        IsEligibleForSelfService = isEligibleForSelfService;
        IsDeprecated = isDeprecated;
    }

    /// <summary>
    /// Factory used by seed scripts and Admin handlers (slice 0f+). Validates
    /// mandatory fields and the price is in range.
    /// </summary>
    public static Result<Plan> Create(
        Guid id,
        PlanCode code,
        string name,
        Money monthlyPrice,
        bool isEligibleForSelfService)
    {
        if (id == Guid.Empty)
            return Result.Failure<Plan>(BillingDomainErrors.Subscription.IdRequired);

        if (code is null)
            return Result.Failure<Plan>(BillingDomainErrors.Plan.CodeRequired);

        if (string.IsNullOrWhiteSpace(name) || name.Trim().Length < 2)
            return Result.Failure<Plan>(BillingDomainErrors.Plan.NameRequired);

        if (monthlyPrice is null)
            return Result.Failure<Plan>(Error.Validation(
                "plan.price_required", "Plan monthly price is required."));

        return Result.Success(new Plan(
            id, code, name.Trim(), monthlyPrice, isEligibleForSelfService, isDeprecated: false));
    }

    /// <summary>
    /// Direct construction without validation. Use ONLY from trusted sources
    /// (seed scripts, EF hydration). Production handlers go through
    /// <see cref="Create"/>.
    /// </summary>
    public static Plan FromTrusted(
        Guid id,
        PlanCode code,
        string name,
        Money monthlyPrice,
        bool isEligibleForSelfService,
        bool isDeprecated)
        => new(id, code, name, monthlyPrice, isEligibleForSelfService, isDeprecated);
}
