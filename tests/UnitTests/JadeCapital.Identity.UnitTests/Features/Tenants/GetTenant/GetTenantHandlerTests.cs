using JadeCapital.Identity.Application.Abstractions;
using JadeCapital.Identity.Application.Features.Tenants.GetTenant;
using JadeCapital.Identity.Domain.Tenants;
using JadeCapital.Shared.Kernel.MultiTenancy;
using JadeCapital.Shared.Kernel.Time;
using NSubstitute.ReturnsExtensions;

namespace JadeCapital.Identity.UnitTests.Features.Tenants.GetTenant;

/// <summary>
/// Tests for <see cref="GetTenantHandler"/> (Wave 6, slice 6c.1).
///
/// <para>
/// Three RED scenarios pinned here:
/// </para>
/// <list type="number">
///   <item>Caller is the owner → 200 with the DTO</item>
///   <item>Caller is in a different tenant → 404 <c>tenant.not_found</c> (don't leak existence)</item>
///   <item>Tenant doesn't exist → 404 <c>tenant.not_found</c></item>
/// </list>
///
/// <para>
/// Slice 6c.1 uses an explicit <c>CurrentUserId</c> in the query (the
/// real JWT-derived resolution lands in 6c.2). The handler still consults
/// <see cref="ITenantContext.CurrentUserId"/> as the source of truth so the
/// 6c.2 swap is a one-line change.
/// </para>
/// </summary>
public class GetTenantHandlerTests
{
    private readonly ITenantRepository _tenants = Substitute.For<ITenantRepository>();
    private readonly IUserRepository _users = Substitute.For<IUserRepository>();
    private readonly ITenantContext _tenantContext = Substitute.For<ITenantContext>();
    private readonly IClock _clock = Substitute.For<IClock>();

    private GetTenantHandler CreateSut() => new(_tenants, _users, _tenantContext, _clock);

    private static readonly DateTimeOffset FixedNow = new(2026, 8, 19, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Handle_OwnerRequestsOwnTenant_Returns200()
    {
        var ownerId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(FixedNow);
        var tenant = Tenant.Create(tenantId, "Acme", "acme", ownerId, TenantPlan.Personal, clock).Value;
        _tenants.GetByIdAsync(tenantId, Arg.Any<CancellationToken>()).Returns(tenant);

        var query = new GetTenantQuery(tenantId, ActingUserId: ownerId);
        var result = await CreateSut().Handle(query, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        var dto = result.Value;
        dto.Id.Should().Be(tenantId);
        dto.OwnerUserId.Should().Be(ownerId);
        dto.Slug.Should().Be("acme");
        dto.Plan.Should().Be("Personal");
        dto.Status.Should().Be("Active");
    }

    [Fact]
    public async Task Handle_CallerIsNotOwner_ReturnsNotFound()
    {
        var ownerId = Guid.NewGuid();
        var callerId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(FixedNow);
        var tenant = Tenant.Create(tenantId, "Acme", "acme", ownerId, TenantPlan.Personal, clock).Value;
        _tenants.GetByIdAsync(tenantId, Arg.Any<CancellationToken>()).Returns(tenant);

        var query = new GetTenantQuery(tenantId, ActingUserId: callerId);
        var result = await CreateSut().Handle(query, CancellationToken.None);

        // Slice 6c.1 deliberately returns 404 for cross-tenant — don't leak
        // existence (a 403 would confirm the tenant id is real).
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("notfound.tenant.not_found");
    }

    [Fact]
    public async Task Handle_TenantDoesNotExist_ReturnsNotFound()
    {
        var tenantId = Guid.NewGuid();
        _tenants.GetByIdAsync(tenantId, Arg.Any<CancellationToken>()).ReturnsNull();

        var query = new GetTenantQuery(tenantId);
        var result = await CreateSut().Handle(query, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("notfound.tenant.not_found");
    }
}
