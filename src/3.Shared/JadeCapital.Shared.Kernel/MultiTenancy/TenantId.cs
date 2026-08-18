namespace JadeCapital.Shared.Kernel.MultiTenancy;

/// <summary>
/// Strongly-typed <see cref="Guid"/> wrapper for a <c>identity.tenants.id</c>
/// value (Wave 6, slice 6c.1).
///
/// <para>
/// <b>Why a wrapper?</b> Handler signatures become self-documenting:
/// <c>GetTenantAsync(TenantId id)</c> vs <c>GetTenantAsync(Guid id)</c> — the
/// compiler refuses the latter when a user id is in scope, eliminating a class
/// of cross-tenant bugs at compile time. Equality is value-based via the
/// record primary constructor.
/// </para>
///
/// <para>
/// <b>Persistence</b>: EF Core stores the inner Guid directly (no converter
/// needed) because <see cref="Value"/> is the only payload. The
/// <c>tenants.id</c> column is a raw <c>UUID</c> in PostgreSQL.
/// </para>
///
/// <para>
/// <b>JSON</b>: serialized as a single <c>{"value":"..."}</c> object to keep
/// the wire shape extensible (we can add fields later without breaking
/// existing clients). The contract is pinned by
/// <c>Shared.Kernel.UnitTests/MultiTenancy/TenantIdTests.JsonContract_*</c>.
/// </para>
/// </summary>
public sealed record TenantId(Guid Value)
{
    /// <summary>Sentinel for "no tenant assigned". Maps to <see cref="Guid.Empty"/>.</summary>
    public static TenantId Empty => new(Guid.Empty);

    /// <summary>Factory: generates a fresh <see cref="TenantId"/> from <see cref="Guid.NewGuid"/>.</summary>
    public static TenantId New() => new(Guid.NewGuid());

    /// <summary>Renders the inner Guid as a string. Round-trips via <see cref="Guid.Parse(string)"/>.</summary>
    public override string ToString() => Value.ToString();
}
