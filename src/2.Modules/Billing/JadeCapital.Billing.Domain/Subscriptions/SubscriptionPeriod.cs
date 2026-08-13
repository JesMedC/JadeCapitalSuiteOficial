using JadeCapital.Shared.Kernel.Results;

namespace JadeCapital.Billing.Domain.Subscriptions;

/// <summary>
/// Billing period boundaries — inclusive start, exclusive end. Validates that
/// the period is forward-progressing and lasts at least one day.
/// </summary>
/// <remarks>
/// Equality by <c>Start</c> + <c>End</c>. The aggregate treats the period as
/// a stable identity for renewal cycles (a fresh period replaces the prior
/// one with a new <c>Guid</c>, not via mutation of <c>End</c>).
/// </remarks>
public sealed class SubscriptionPeriod
{
    public DateTimeOffset Start { get; }
    public DateTimeOffset End { get; }

    private SubscriptionPeriod(DateTimeOffset start, DateTimeOffset end)
    {
        Start = start;
        End = end;
    }

    public static Result<SubscriptionPeriod> Create(DateTimeOffset start, DateTimeOffset end)
    {
        if (end <= start)
            return Result.Failure<SubscriptionPeriod>(Error.Validation(
                "subscription_period.invalid_range", "Period end must be after start."));

        if (end - start < TimeSpan.FromDays(1))
            return Result.Failure<SubscriptionPeriod>(Error.Validation(
                "subscription_period.too_short", "Period must be at least one day."));

        return Result.Success(new SubscriptionPeriod(start, end));
    }

    /// <summary>
    /// Direct construction without validation. Use ONLY from trusted sources
    /// (seed scripts, EF hydration). Production handlers go through
    /// <see cref="Create"/>.
    /// </summary>
    public static SubscriptionPeriod FromTrusted(DateTimeOffset start, DateTimeOffset end)
        => new(start, end);

    public bool Contains(DateTimeOffset utcNow)
        => utcNow >= Start && utcNow < End;

    public override string ToString() => $"{Start:O} → {End:O}";
}
