using JadeCapital.Billing.Domain.Common;
using JadeCapital.Shared.Kernel.Primitives;
using JadeCapital.Shared.Kernel.Results;

namespace JadeCapital.Billing.Domain.Subscriptions;

/// <summary>
/// Plan identifier (e.g. "starter", "pro", "enterprise"). Normalised to
/// lowercase, 2-32 chars. Equality by value.
/// </summary>
public sealed class PlanCode : ValueObject
{
    public string Value { get; }

    private PlanCode(string value)
    {
        Value = value;
    }

    public static Result<PlanCode> Create(string? code)
    {
        if (string.IsNullOrWhiteSpace(code))
            return Result.Failure<PlanCode>(BillingDomainErrors.Plan.CodeRequired);

        var normalised = code.Trim().ToLowerInvariant();
        if (normalised.Length is < 2 or > 32)
            return Result.Failure<PlanCode>(BillingDomainErrors.Plan.CodeInvalidLength);

        return Result.Success(new PlanCode(normalised));
    }

    /// <summary>
    /// Direct construction without validation. Use ONLY from trusted sources
    /// (seed scripts, EF hydration, infrastructure fixtures). Production
    /// handlers go through <see cref="Create"/>.
    /// </summary>
    public static PlanCode FromTrusted(string code) => new(code.Trim().ToLowerInvariant());

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Value;
    }

    public override string ToString() => Value;
}
