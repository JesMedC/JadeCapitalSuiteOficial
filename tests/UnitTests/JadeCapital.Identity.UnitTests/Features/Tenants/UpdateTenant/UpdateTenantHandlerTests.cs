using JadeCapital.Identity.Application.Abstractions;
using JadeCapital.Identity.Application.Features.Tenants.UpdateTenant;
using JadeCapital.Identity.Domain.Tenants;
using JadeCapital.Shared.Kernel.MultiTenancy;
using JadeCapital.Shared.Kernel.Time;
using NSubstitute.ReturnsExtensions;

namespace JadeCapital.Identity.UnitTests.Features.Tenants.UpdateTenant;

/// <summary>
/// Tests for <see cref="UpdateTenantHandler"/> (Wave 6, slice 6c.3).
///
/// <para>
/// Four RED scenarios pinned here (per tasks.md 6c.3 line 366):
/// </para>
/// <list type="number">
///   <item>Valid update by owner → 200 + DTO reflects new name</item>
///   <item>Cross-tenant update → 404 <c>tenant.not_found</c> (security best practice: don't leak existence)</item>
///   <item>Suspended tenant → 422 <c>tenant.cannot_modify_suspended</c> (invariant from design.md)</item>
///   <item>Invalid name → 422 <c>tenant.name_required</c> / <c>tenant.name_too_short</c></item>
/// </list>
///
/// <para>
/// Slice 6c.3 uses <see cref="ITenantContext.Current"/> as the source of
/// truth for the caller's tenant — the JWT-derived resolution from 6c.2.
/// Tests inject <see cref="ITenantContext"/> via NSubstitute so we exercise
/// the cross-tenant 404 path with the real production contract (not a
/// query-arg shortcut).
/// </para>
/// </summary>
public class UpdateTenantHandlerTests
{
    private readonly ITenantRepository _tenants = Substitute.For<ITenantRepository>();
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();
    private readonly ITenantContext _tenantContext = Substitute.For<ITenantContext>();
    private readonly IClock _clock = Substitute.For<IClock>();

    private UpdateTenantHandler CreateSut() => new(_tenants, _uow, _tenantContext, _clock);

    private static readonly DateTimeOffset FixedNow = new(2026, 8, 19, 12, 0, 0, TimeSpan.Zero);

    private static Tenant CreateActiveTenant(Guid ownerId, IClock clock, string name = "Acme", string slug = "acme")
        => Tenant.Create(Guid.NewGuid(), name, slug, ownerId, TenantPlan.Personal, clock).Value;

    [Fact]
    public async Task Handle_OwnerUpdatesValidName_ReturnsUpdatedDto()
    {
        var ownerId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(FixedNow);
        var tenant = CreateActiveTenant(ownerId, clock);
        _tenants.GetByIdAsync(tenantId, Arg.Any<CancellationToken>()).Returns(tenant);
        _tenantContext.Current.Returns(new TenantId(tenant.Id));
        _tenantContext.IsSuperAdmin.Returns(false);
        _uow.SaveChangesAsync(Arg.Any<CancellationToken>()).Returns(Result.Success(1));

        var cmd = new UpdateTenantCommand(tenantId, Name: "Acme Renamed", Plan: null);
        var result = await CreateSut().Handle(cmd, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Name.Should().Be("Acme Renamed");
        await _uow.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_CallerInDifferentTenant_ReturnsNotFound()
    {
        var ownerId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var callerTenantId = new TenantId(Guid.NewGuid());
        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(FixedNow);
        var tenant = CreateActiveTenant(ownerId, clock);
        _tenants.GetByIdAsync(tenantId, Arg.Any<CancellationToken>()).Returns(tenant);
        // Caller's tenant differs from the target — security best practice: 404 not 403.
        _tenantContext.Current.Returns(callerTenantId);

        var cmd = new UpdateTenantCommand(tenantId, Name: "Pwned", Plan: null);
        var result = await CreateSut().Handle(cmd, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("notfound.tenant.not_found");
        await _uow.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_SuspendedTenant_ReturnsUnprocessable()
    {
        var ownerId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(FixedNow);
        var tenant = CreateActiveTenant(ownerId, clock);
        // Suspend via the public API to keep the invariant test realistic.
        tenant.ForceSetStatusForTests(TenantStatus.Suspended);
        _tenants.GetByIdAsync(tenantId, Arg.Any<CancellationToken>()).Returns(tenant);
        _tenantContext.Current.Returns(new TenantId(tenant.Id));
        _tenantContext.IsSuperAdmin.Returns(false);

        var cmd = new UpdateTenantCommand(tenantId, Name: "Acme New", Plan: null);
        var result = await CreateSut().Handle(cmd, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("validation.tenant.cannot_modify_suspended");
        await _uow.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_InvalidName_ReturnsValidationError()
    {
        var ownerId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(FixedNow);
        var tenant = CreateActiveTenant(ownerId, clock);
        _tenants.GetByIdAsync(tenantId, Arg.Any<CancellationToken>()).Returns(tenant);
        _tenantContext.Current.Returns(new TenantId(tenant.Id));
        _tenantContext.IsSuperAdmin.Returns(false);

        // Empty name (whitespace only) — Tenant.Rename rejects it.
        var cmd = new UpdateTenantCommand(tenantId, Name: "  ", Plan: null);
        var result = await CreateSut().Handle(cmd, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("validation.tenant.name_required");
        await _uow.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_NoOpUpdate_ReturnsDtoWithoutSaveChanges()
    {
        var ownerId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(FixedNow);
        var tenant = CreateActiveTenant(ownerId, clock, name: "Acme Original");
        _tenants.GetByIdAsync(tenantId, Arg.Any<CancellationToken>()).Returns(tenant);
        _tenantContext.Current.Returns(new TenantId(tenant.Id));
        _tenantContext.IsSuperAdmin.Returns(false);

        // Both Name and Plan null → no-op. Handler MUST short-circuit
        // before SaveChangesAsync so the UpdatedAt field doesn't get
        // bumped gratuitously.
        var cmd = new UpdateTenantCommand(tenantId, Name: null, Plan: null);
        var result = await CreateSut().Handle(cmd, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Name.Should().Be("Acme Original");
        await _uow.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_SuperAdminBypassesCrossTenantCheck()
    {
        var ownerId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(FixedNow);
        var tenant = CreateActiveTenant(ownerId, clock);
        _tenants.GetByIdAsync(tenantId, Arg.Any<CancellationToken>()).Returns(tenant);
        // SuperAdmin with a DIFFERENT tenant_id in their JWT — must still succeed.
        _tenantContext.Current.Returns(new TenantId(Guid.NewGuid()));
        _tenantContext.IsSuperAdmin.Returns(true);
        _uow.SaveChangesAsync(Arg.Any<CancellationToken>()).Returns(Result.Success(1));

        var cmd = new UpdateTenantCommand(tenantId, Name: "Acme Admin Override", Plan: null);
        var result = await CreateSut().Handle(cmd, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Name.Should().Be("Acme Admin Override");
        await _uow.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }
}
