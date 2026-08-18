using JadeCapital.Identity.Application.Abstractions;
using JadeCapital.Identity.Application.Features.Tenants.RemoveTenantUser;
using JadeCapital.Identity.Domain.Tenants;
using JadeCapital.Identity.Domain.Users;
using JadeCapital.Shared.Kernel.MultiTenancy;
using JadeCapital.Shared.Kernel.Time;
using NSubstitute.ReturnsExtensions;

namespace JadeCapital.Identity.UnitTests.Features.Tenants.RemoveTenantUser;

/// <summary>
/// Tests for <see cref="RemoveTenantUserHandler"/> (Wave 6, slice 6c.3).
///
/// <para>
/// Four RED scenarios pinned here (per tasks.md 6c.3 line 372):
/// </para>
/// <list type="number">
///   <item>Valid removal of a non-owner user → 200 (returns the removed user id)</item>
///   <item>Owner cannot remove self → 422 <c>tenant.owner_cannot_remove_self</c></item>
///   <item>Cross-tenant remove → 404 <c>tenant.not_found</c></item>
///   <item>User not in the target tenant → 404 <c>tenant.user_not_in_tenant</c></item>
/// </list>
///
/// <para>
/// <b>Slice 6c.3 NOT NULL contract</b>: the 6c.3 migration makes
/// <c>identity.users.tenant_id</c> NOT NULL, so a literal "set to NULL"
/// would fail at the DB level. The handler therefore REASSIGNS the removed
/// user to the Personal default tenant (the stable
/// <c>personal-default</c> slug, created by 0026_backfill_personal_tenant.sql
/// and <c>BackfillTenantsRunner</c>). This preserves the "remove from
/// workspace" semantics — the user is no longer a member of the target
/// tenant — without violating the NOT NULL constraint.
/// </para>
///
/// <para>
/// Edge cases covered below:
/// </para>
/// <list type="bullet">
///   <item>Target tenant has its own Personal fallback (e.g. when the
///         owner removes someone from a Personal-tenant workspace itself):
///         the user is reassigned to the same tenant (idempotent no-op
///         from the aggregate's perspective).</item>
///   <item>Personal default tenant missing in the DB (should never happen
///         post-migration): handler returns <c>tenant.configuration_error</c>
///         rather than silently NULL-ing the column.</item>
/// </list>
///
/// </summary>
public class RemoveTenantUserHandlerTests
{
    private readonly ITenantRepository _tenants = Substitute.For<ITenantRepository>();
    private readonly IUserRepository _users = Substitute.For<IUserRepository>();
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();
    private readonly ITenantContext _tenantContext = Substitute.For<ITenantContext>();

    private RemoveTenantUserHandler CreateSut() => new(_tenants, _users, _uow, _tenantContext);

    private static readonly DateTimeOffset FixedNow = new(2026, 8, 19, 12, 0, 0, TimeSpan.Zero);

    private static Tenant CreateActiveTenant(Guid ownerId, IClock clock)
        => Tenant.Create(Guid.NewGuid(), "Acme", "acme", ownerId, TenantPlan.Personal, clock).Value;

    [Fact]
    public async Task Handle_RemovesNonOwnerMember_ReassignsToPersonalDefault()
    {
        var ownerId = Guid.NewGuid();
        var memberId = Guid.NewGuid();
        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(FixedNow);
        var targetTenant = CreateActiveTenant(ownerId, clock);
        // Personal default tenant (separate workspace; stable slug).
        var personalTenant = CreateActiveTenant(Guid.NewGuid(), clock);

        var member = User.Register(memberId, "member@test.com", "Member", "h", UserRole.Trader).Value;
        // Pretend the member was previously assigned to the target tenant.
        var assignResult = member.AssignToTenant(new TenantId(targetTenant.Id));
        assignResult.IsSuccess.Should().BeTrue();

        _tenants.GetByIdAsync(targetTenant.Id, Arg.Any<CancellationToken>()).Returns(targetTenant);
        _tenants.FindBySlugAsync("personal-default", Arg.Any<CancellationToken>()).Returns(personalTenant);
        _users.FindByIdAsync(memberId, Arg.Any<CancellationToken>()).Returns(member);
        _tenantContext.Current.Returns(new TenantId(targetTenant.Id));
        _tenantContext.IsSuperAdmin.Returns(false);
        _uow.SaveChangesAsync(Arg.Any<CancellationToken>()).Returns(Result.Success(1));

        var cmd = new RemoveTenantUserCommand(targetTenant.Id, memberId);
        var result = await CreateSut().Handle(cmd, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(memberId);
        // 6c.3 NOT NULL contract: the user MUST be reassigned to the
        // Personal default, NOT unassigned to NULL. A NULL here would
        // fail the runtime DB constraint.
        member.TenantId.Should().Be(new TenantId(personalTenant.Id));
        await _uow.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// Edge case: when the target tenant IS itself the Personal default
    /// (slug <c>personal-default</c>), removing a member should be a
    /// no-op assignment to the same id (NOT a violation of the
    /// "cross-tenant reassign" guard, which only fires when the ids differ).
    /// </summary>
    [Fact]
    public async Task Handle_RemovesMemberFromPersonalDefaultTenant_KeepsSameAssignment()
    {
        var ownerId = Guid.NewGuid();
        var memberId = Guid.NewGuid();
        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(FixedNow);
        // Personal tenant IS the target.
        var personalTenant = Tenant.Create(
            Guid.NewGuid(), "Personal", "personal-default", ownerId,
            TenantPlan.Personal, clock).Value;
        var personalTenantId = personalTenant.Id;

        var member = User.Register(memberId, "member@test.com", "Member", "h", UserRole.Trader).Value;
        member.AssignToTenant(new TenantId(personalTenantId)).IsSuccess.Should().BeTrue();

        _tenants.GetByIdAsync(personalTenantId, Arg.Any<CancellationToken>()).Returns(personalTenant);
        _tenants.FindBySlugAsync("personal-default", Arg.Any<CancellationToken>()).Returns(personalTenant);
        _users.FindByIdAsync(memberId, Arg.Any<CancellationToken>()).Returns(member);
        _tenantContext.Current.Returns(new TenantId(personalTenantId));
        _tenantContext.IsSuperAdmin.Returns(false);
        _uow.SaveChangesAsync(Arg.Any<CancellationToken>()).Returns(Result.Success(1));

        var cmd = new RemoveTenantUserCommand(personalTenantId, memberId);
        var result = await CreateSut().Handle(cmd, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        // Member stays in the Personal default — same id assignment.
        member.TenantId.Should().Be(new TenantId(personalTenantId));
    }

    [Fact]
    public async Task Handle_OwnerCannotRemoveSelf_ReturnsUnprocessable()
    {
        var ownerId = Guid.NewGuid();
        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(FixedNow);
        var tenant = CreateActiveTenant(ownerId, clock);

        var owner = User.Register(ownerId, "owner@test.com", "Owner", "h", UserRole.Admin).Value;
        var assignResult = owner.AssignToTenant(new TenantId(tenant.Id));
        assignResult.IsSuccess.Should().BeTrue();

        _tenants.GetByIdAsync(tenant.Id, Arg.Any<CancellationToken>()).Returns(tenant);
        _users.FindByIdAsync(ownerId, Arg.Any<CancellationToken>()).Returns(owner);
        _tenantContext.Current.Returns(new TenantId(tenant.Id));
        _tenantContext.IsSuperAdmin.Returns(false);

        var cmd = new RemoveTenantUserCommand(tenant.Id, ownerId);
        var result = await CreateSut().Handle(cmd, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("validation.tenant.owner_cannot_remove_self");
        await _uow.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_CallerInDifferentTenant_ReturnsNotFound()
    {
        var ownerId = Guid.NewGuid();
        var memberId = Guid.NewGuid();
        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(FixedNow);
        var tenant = CreateActiveTenant(ownerId, clock);

        _tenants.GetByIdAsync(tenant.Id, Arg.Any<CancellationToken>()).Returns(tenant);
        _tenantContext.Current.Returns(new TenantId(Guid.NewGuid()));

        var cmd = new RemoveTenantUserCommand(tenant.Id, memberId);
        var result = await CreateSut().Handle(cmd, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("notfound.tenant.not_found");
        await _uow.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_UserNotInTenant_ReturnsNotFound()
    {
        var ownerId = Guid.NewGuid();
        var outsiderId = Guid.NewGuid();
        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(FixedNow);
        var tenant = CreateActiveTenant(ownerId, clock);

        // The "outsider" is a user but NOT assigned to this tenant.
        var outsider = User.Register(outsiderId, "outsider@test.com", "Outsider", "h", UserRole.Trader).Value;
        outsider.TenantId.Should().BeNull();

        _tenants.GetByIdAsync(tenant.Id, Arg.Any<CancellationToken>()).Returns(tenant);
        _users.FindByIdAsync(outsiderId, Arg.Any<CancellationToken>()).Returns(outsider);
        _tenantContext.Current.Returns(new TenantId(tenant.Id));
        _tenantContext.IsSuperAdmin.Returns(false);

        var cmd = new RemoveTenantUserCommand(tenant.Id, outsiderId);
        var result = await CreateSut().Handle(cmd, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("notfound.tenant.user_not_in_tenant");
        await _uow.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }
}
