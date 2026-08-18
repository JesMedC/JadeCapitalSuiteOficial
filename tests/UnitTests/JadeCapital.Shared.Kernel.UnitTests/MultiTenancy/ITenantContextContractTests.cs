using FluentAssertions;
using JadeCapital.Shared.Kernel.MultiTenancy;

namespace JadeCapital.Shared.Kernel.UnitTests.MultiTenancy;

/// <summary>
/// Contract tests for <see cref="ITenantContext"/> (Wave 6, slice 6c.1).
///
/// <para>
/// The interface pins the cross-module read surface for the current request's
/// tenant / user. Three RED scenarios pinned here:
/// </para>
/// <list type="number">
///   <item>Interface shape: exactly three members (<c>Current</c>,
///   <c>CurrentUserId</c>, <c>IsSuperAdmin</c>) — no accidental expansion</item>
///   <item><c>Current</c> and <c>CurrentUserId</c> are nullable for anonymous
///   callers</item>
///   <item><c>IsSuperAdmin</c> defaults to <c>false</c> for non-admin callers
///   (a placeholder impl lands in 6c.1; the real JWT-derived impl ships in 6c.2)</item>
/// </list>
///
/// <para>
/// Changing the shape (renaming, adding, removing members) is a breaking change
/// for every consumer. Pin early.
/// </para>
/// </summary>
public class ITenantContextContractTests
{
    [Fact]
    public void ITenantContext_DeclaresExactlyThreeMembers()
    {
        var iface = typeof(ITenantContext);
        var members = iface.GetProperties()
            .Select(p => p.Name)
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToArray();

        members.Should().BeEquivalentTo(new[]
        {
            "Current",
            "CurrentUserId",
            "IsSuperAdmin"
        });
    }

    [Fact]
    public void Current_AndCurrentUserId_AreNullable()
    {
        var iface = typeof(ITenantContext);

        var currentProp = iface.GetProperty("Current")!;
        var userProp = iface.GetProperty("CurrentUserId")!;

        // TenantId is a reference type (record) → null annotation is allowed
        // at the call site; reflection reports the bare type.
        currentProp.PropertyType.Should().Be(typeof(TenantId),
            "Current returns TenantId (reference type); nullability annotation is at the call site.");

        // Guid? is Nullable<Guid>; reflection reports Nullable<Guid>.
        Nullable.GetUnderlyingType(userProp.PropertyType).Should().Be(typeof(Guid),
            $"CurrentUserId must be Guid? for anonymous callers. Actual: {userProp.PropertyType.FullName}");
    }

    [Fact]
    public void IsSuperAdmin_DefaultsToFalse_InPlaceholder()
    {
        // Slice 6c.1 placeholder: ITenantContext is wired into DI with a no-op
        // implementation that returns null/false for all members. The real
        // JWT-derived impl lands in slice 6c.2. This test pins that contract:
        // a placeholder must NOT grant super-admin by default.
        ITenantContext placeholder = new PlaceholderTenantContext();

        placeholder.IsSuperAdmin.Should().BeFalse();
        placeholder.Current.Should().BeNull();
        placeholder.CurrentUserId.Should().BeNull();
    }

    /// <summary>
    /// Placeholder impl used to pin the default-behavior contract. The real
    /// implementation is in slice 6c.2 (HttpContext-bound, JWT-claim-driven).
    /// </summary>
    private sealed class PlaceholderTenantContext : ITenantContext
    {
        public TenantId? Current => null;
        public Guid? CurrentUserId => null;
        public bool IsSuperAdmin => false;
    }
}
