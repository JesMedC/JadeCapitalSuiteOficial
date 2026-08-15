namespace JadeCapital.Billing.Domain.Common;

/// <summary>
/// Generates monotonically-increasing <see cref="Guid"/> identifiers so that
/// aggregate-internal list ordering (e.g. subscription history) can rely on
/// <c>Guid.CompareTo</c> to break timestamp ties deterministically. The packed
/// 64-bit counter occupies the most-significant bytes of the Guid layout, so
/// newer identifiers compare greater than older ones via the standard
/// <see cref="Guid.CompareTo"/> implementation.
/// </summary>
/// <remarks>
/// Process-lifetime uniqueness is sufficient for the bounded context: each
/// aggregate that emits ordered child identifiers (Plan, Subscription)
/// creates exactly the number of identifiers it owns. The DB column is
/// <c>UUID</c> with the default Postgres generator; this helper only
/// matters for in-memory ordering during a single domain session, mirroring
/// the EF/SQL tie-breaker documented in design.md.
/// </remarks>
internal static class MonotonicGuid
{
    private static long _counter;

    public static Guid NewId()
    {
        var c = Interlocked.Increment(ref _counter);
        var bytes = new byte[16];
        BitConverter.GetBytes(c).CopyTo(bytes, 0);   // 8-byte counter at the high slot
        for (var i = 8; i < 16; i++) bytes[i] = 0xFF; // tail fills with high values to keep
                                                       // uniqueness if the counter wraps
        return new Guid(bytes);
    }
}
