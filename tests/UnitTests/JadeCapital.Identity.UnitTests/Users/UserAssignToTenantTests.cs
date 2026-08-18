using FluentAssertions;
using JadeCapital.Identity.Domain.Users;
using JadeCapital.Shared.Kernel.MultiTenancy;
using JadeCapital.Shared.Kernel.Time;

namespace JadeCapital.Identity.UnitTests.Users;

/// <summary>
/// Tests for <see cref="User.AssignToTenant"/> (Wave 6, slice 6c.1).
///
/// <para>
/// Four RED scenarios pinned here:
/// </para>
/// <list type="number">
///   <item>Assign a valid <see cref="TenantId"/> → <c>User.TenantId</c> is set; <c>UpdatedAt</c> bumped</item>
///   <item>Re-assign the same <see cref="TenantId"/> → no-op (idempotent, no error)</item>
///   <item>Cross-tenant re-assign requires Admin role; Trader gets a 403</item>
///   <item>Assigning <see cref="TenantId.Empty"/> → 422 <c>validation.user.tenant_id_invalid</c></item>
/// </list>
/// </summary>
public class UserAssignToTenantTests
{
    private static readonly DateTimeOffset FixedNow = new(2026, 8, 19, 12, 0, 0, TimeSpan.Zero);

    private static User CreateUser(UserRole role = UserRole.Trader, string email = "user@test.com")
    {
        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(FixedNow);
        return User.Register(Guid.NewGuid(), email, "User", "h", role).Value;
    }

    [Fact]
    public void AssignToTenant_ValidTenant_SetsTenantId()
    {
        var user = CreateUser();
        var tenantId = TenantId.New();

        var result = user.AssignToTenant(tenantId);

        result.IsSuccess.Should().BeTrue();
        user.TenantId.Should().Be(tenantId);
        user.UpdatedAt.Should().NotBeNull();
    }

    [Fact]
    public void AssignToTenant_SameTenantId_IsNoOp()
    {
        var user = CreateUser();
        var tenantId = TenantId.New();

        user.AssignToTenant(tenantId).IsSuccess.Should().BeTrue();
        var updatedAtAfterFirst = user.UpdatedAt;

        // Re-assign to the same tenant — idempotent, no error, no state change.
        var result = user.AssignToTenant(tenantId);
        result.IsSuccess.Should().BeTrue();
        user.TenantId.Should().Be(tenantId);
        user.UpdatedAt.Should().Be(updatedAtAfterFirst, "no-op must not bump UpdatedAt");
    }

    [Fact]
    public void AssignToTenant_CrossTenant_RequiresAdmin()
    {
        var user = CreateUser(role: UserRole.Trader);
        var firstTenant = TenantId.New();
        var secondTenant = TenantId.New();

        user.AssignToTenant(firstTenant).IsSuccess.Should().BeTrue();

        // Trader trying to switch to a different tenant → forbidden.
        var result = user.AssignToTenant(secondTenant);
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("forbidden.user.cross_tenant_reassign_requires_admin");
        user.TenantId.Should().Be(firstTenant,
            "the failed re-assign must not mutate state");
    }

    [Fact]
    public void AssignToTenant_CrossTenant_AdminSucceeds()
    {
        var user = CreateUser(role: UserRole.Admin);
        var firstTenant = TenantId.New();
        var secondTenant = TenantId.New();

        user.AssignToTenant(firstTenant).IsSuccess.Should().BeTrue();

        var result = user.AssignToTenant(secondTenant);
        result.IsSuccess.Should().BeTrue();
        user.TenantId.Should().Be(secondTenant);
    }

    [Fact]
    public void AssignToTenant_EmptyTenantId_ReturnsValidationError()
    {
        var user = CreateUser();

        var result = user.AssignToTenant(TenantId.Empty);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("validation.user.tenant_id_invalid");
    }
}
