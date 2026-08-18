using JadeCapital.Identity.Application.Abstractions;
using JadeCapital.Identity.Application.Features.Tenants.CreateTenant;
using JadeCapital.Identity.Domain.Tenants;
using JadeCapital.Identity.Domain.Tenants.Events;
using JadeCapital.Identity.Domain.Users;
using JadeCapital.Shared.Kernel.Time;
using NSubstitute.ReturnsExtensions;

namespace JadeCapital.Identity.UnitTests.Features.Tenants.CreateTenant;

/// <summary>
/// Tests for <see cref="CreateTenantHandler"/> (Wave 6, slice 6c.1).
///
/// <para>
/// Five RED scenarios pinned here:
/// </para>
/// <list type="number">
///   <item>Valid request → tenant created + persisted + TenantCreatedDomainEvent emitted</item>
///   <item>Slug already exists → <c>tenant.slug_taken</c> (409)</item>
///   <item>Owner user not found → <c>tenant.owner_not_found</c> (404)</item>
///   <item>Name empty → <c>tenant.name_required</c> (422)</item>
///   <item>Slug with invalid chars → <c>tenant.slug_format_invalid</c> (422)</item>
/// </list>
/// </summary>
public class CreateTenantHandlerTests
{
    private readonly ITenantRepository _tenants = Substitute.For<ITenantRepository>();
    private readonly IUserRepository _users = Substitute.For<IUserRepository>();
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();
    private readonly IClock _clock = Substitute.For<IClock>();

    private CreateTenantHandler CreateSut() => new(_tenants, _users, _uow, _clock);

    private static readonly DateTimeOffset FixedNow = new(2026, 8, 19, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Handle_ValidRequest_CreatesTenantAndPersists()
    {
        var ownerId = Guid.NewGuid();
        var owner = User.Register(ownerId, "owner@test.com", "Owner", "h", UserRole.Trader).Value;
        _users.FindByIdAsync(ownerId, Arg.Any<CancellationToken>()).Returns(owner);
        _tenants.FindBySlugAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).ReturnsNull();
        _clock.UtcNow.Returns(FixedNow);
        _uow.SaveChangesAsync(Arg.Any<CancellationToken>())
            .Returns(Result.Success(1));

        var cmd = new CreateTenantCommand("Acme Capital", "acme-capital", ownerId);
        var result = await CreateSut().Handle(cmd, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        var dto = result.Value;
        dto.Name.Should().Be("Acme Capital");
        dto.Slug.Should().Be("acme-capital");
        dto.OwnerUserId.Should().Be(ownerId);
        dto.Plan.Should().Be("Personal");
        dto.Status.Should().Be("Active");

        await _tenants.Received(1).AddAsync(
            Arg.Is<Tenant>(t => t.Slug == "acme-capital" && t.OwnerUserId == ownerId),
            Arg.Any<CancellationToken>());
        await _uow.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_SlugAlreadyExists_ReturnsConflict()
    {
        var ownerId = Guid.NewGuid();
        var owner = User.Register(ownerId, "owner@test.com", "Owner", "h", UserRole.Trader).Value;
        var existingClock = Substitute.For<IClock>();
        existingClock.UtcNow.Returns(FixedNow);
        _users.FindByIdAsync(ownerId, Arg.Any<CancellationToken>()).Returns(owner);

        var existing = Tenant.Create(
            Guid.NewGuid(), "Existing", "acme-capital", ownerId,
            TenantPlan.Personal, existingClock).Value;
        _tenants.FindBySlugAsync("acme-capital", Arg.Any<CancellationToken>()).Returns(existing);

        var cmd = new CreateTenantCommand("Acme Capital", "acme-capital", ownerId);
        var result = await CreateSut().Handle(cmd, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("conflict.tenant.slug_taken");
        await _tenants.DidNotReceive().AddAsync(Arg.Any<Tenant>(), Arg.Any<CancellationToken>());
        await _uow.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_OwnerNotFound_ReturnsNotFound()
    {
        var ownerId = Guid.NewGuid();
        _users.FindByIdAsync(ownerId, Arg.Any<CancellationToken>()).ReturnsNull();

        var cmd = new CreateTenantCommand("Acme Capital", "acme-capital", ownerId);
        var result = await CreateSut().Handle(cmd, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("notfound.tenant.owner_not_found");
        await _tenants.DidNotReceive().AddAsync(Arg.Any<Tenant>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_EmptyName_ReturnsValidationError()
    {
        var ownerId = Guid.NewGuid();
        var owner = User.Register(ownerId, "owner@test.com", "Owner", "h", UserRole.Trader).Value;
        _users.FindByIdAsync(ownerId, Arg.Any<CancellationToken>()).Returns(owner);

        var cmd = new CreateTenantCommand("", "acme-capital", ownerId);
        var result = await CreateSut().Handle(cmd, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("validation.tenant.name_required");
        await _tenants.DidNotReceive().AddAsync(Arg.Any<Tenant>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_InvalidSlug_ReturnsValidationError()
    {
        var ownerId = Guid.NewGuid();
        var owner = User.Register(ownerId, "owner@test.com", "Owner", "h", UserRole.Trader).Value;
        _users.FindByIdAsync(ownerId, Arg.Any<CancellationToken>()).Returns(owner);

        var cmd = new CreateTenantCommand("Acme Capital", "Bad Slug", ownerId);
        var result = await CreateSut().Handle(cmd, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("validation.tenant.slug_format_invalid");
        await _tenants.DidNotReceive().AddAsync(Arg.Any<Tenant>(), Arg.Any<CancellationToken>());
    }
}
