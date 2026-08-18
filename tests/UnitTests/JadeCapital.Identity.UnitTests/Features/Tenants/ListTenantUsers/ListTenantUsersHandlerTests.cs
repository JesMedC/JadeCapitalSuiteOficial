using JadeCapital.Identity.Application.Abstractions;
using JadeCapital.Identity.Application.Features.Tenants.ListTenantUsers;
using JadeCapital.Identity.Domain.Tenants;
using JadeCapital.Identity.Domain.Users;
using JadeCapital.Shared.Kernel.MultiTenancy;
using JadeCapital.Shared.Kernel.Time;
using NSubstitute.ReturnsExtensions;

namespace JadeCapital.Identity.UnitTests.Features.Tenants.ListTenantUsers;

/// <summary>
/// Tests for <see cref="ListTenantUsersHandler"/> (Wave 6, slice 6c.3).
///
/// <para>
/// Four RED scenarios pinned here (per tasks.md 6c.3 line 368):
/// </para>
/// <list type="number">
///   <item>Owner lists own tenant → 200 + array of users</item>
///   <item>Cross-tenant list → 404 <c>tenant.not_found</c></item>
///   <item>Empty tenant (no other members) → 200 + empty array (NOT 404)</item>
///   <item>Suspended tenant → 422 <c>tenant.cannot_modify_suspended</c></item>
/// </list>
///
/// <para>
/// Reads users from <see cref="IUserRepository.ListByTenantIdAsync"/> — a
/// new contract added in 6c.3 (the existing repo doesn't expose a tenant
/// filter, so this is the first consumer).
/// </para>
/// </summary>
public class ListTenantUsersHandlerTests
{
    private readonly ITenantRepository _tenants = Substitute.For<ITenantRepository>();
    private readonly IUserRepository _users = Substitute.For<IUserRepository>();
    private readonly ITenantContext _tenantContext = Substitute.For<ITenantContext>();

    private ListTenantUsersHandler CreateSut() => new(_tenants, _users, _tenantContext);

    private static readonly DateTimeOffset FixedNow = new(2026, 8, 19, 12, 0, 0, TimeSpan.Zero);

    private static Tenant CreateActiveTenant(Guid ownerId, IClock clock)
        => Tenant.Create(Guid.NewGuid(), "Acme", "acme", ownerId, TenantPlan.Personal, clock).Value;

    [Fact]
    public async Task Handle_OwnerListsOwnTenant_ReturnsUsers()
    {
        var ownerId = Guid.NewGuid();
        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(FixedNow);
        var tenant = CreateActiveTenant(ownerId, clock);
        _tenants.GetByIdAsync(tenant.Id, Arg.Any<CancellationToken>()).Returns(tenant);
        _tenantContext.Current.Returns(new TenantId(tenant.Id));
        _tenantContext.IsSuperAdmin.Returns(false);

        var user1 = User.Register(Guid.NewGuid(), "u1@test.com", "User One", "h", UserRole.Trader).Value;
        var user2 = User.Register(Guid.NewGuid(), "u2@test.com", "User Two", "h", UserRole.Trader).Value;
        _users.ListByTenantIdAsync(new TenantId(tenant.Id), Arg.Any<CancellationToken>())
            .Returns(new[] { user1, user2 });

        var query = new ListTenantUsersQuery(tenant.Id);
        var result = await CreateSut().Handle(query, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().HaveCount(2);
        result.Value.Select(dto => dto.Email).Should().BeEquivalentTo("u1@test.com", "u2@test.com");
    }

    [Fact]
    public async Task Handle_CallerInDifferentTenant_ReturnsNotFound()
    {
        var ownerId = Guid.NewGuid();
        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(FixedNow);
        var tenant = CreateActiveTenant(ownerId, clock);
        _tenants.GetByIdAsync(tenant.Id, Arg.Any<CancellationToken>()).Returns(tenant);
        _tenantContext.Current.Returns(new TenantId(Guid.NewGuid()));

        var query = new ListTenantUsersQuery(tenant.Id);
        var result = await CreateSut().Handle(query, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("notfound.tenant.not_found");
        await _users.DidNotReceive().ListByTenantIdAsync(Arg.Any<TenantId>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_EmptyTenant_ReturnsEmptyArray()
    {
        var ownerId = Guid.NewGuid();
        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(FixedNow);
        var tenant = CreateActiveTenant(ownerId, clock);
        _tenants.GetByIdAsync(tenant.Id, Arg.Any<CancellationToken>()).Returns(tenant);
        _tenantContext.Current.Returns(new TenantId(tenant.Id));
        _tenantContext.IsSuperAdmin.Returns(false);
        _users.ListByTenantIdAsync(new TenantId(tenant.Id), Arg.Any<CancellationToken>())
            .Returns(Array.Empty<JadeCapital.Identity.Domain.Users.User>());

        var query = new ListTenantUsersQuery(tenant.Id);
        var result = await CreateSut().Handle(query, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_SuspendedTenant_ReturnsUnprocessable()
    {
        var ownerId = Guid.NewGuid();
        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(FixedNow);
        var tenant = CreateActiveTenant(ownerId, clock);
        tenant.ForceSetStatusForTests(TenantStatus.Suspended);
        _tenants.GetByIdAsync(tenant.Id, Arg.Any<CancellationToken>()).Returns(tenant);
        _tenantContext.Current.Returns(new TenantId(tenant.Id));
        _tenantContext.IsSuperAdmin.Returns(false);

        var query = new ListTenantUsersQuery(tenant.Id);
        var result = await CreateSut().Handle(query, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("validation.tenant.cannot_modify_suspended");
        await _users.DidNotReceive().ListByTenantIdAsync(Arg.Any<TenantId>(), Arg.Any<CancellationToken>());
    }
}
