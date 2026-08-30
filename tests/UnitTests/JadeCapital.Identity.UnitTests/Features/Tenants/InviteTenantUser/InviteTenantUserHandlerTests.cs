using JadeCapital.Identity.Application.Abstractions;
using JadeCapital.Identity.Application.Features.Tenants.InviteTenantUser;
using JadeCapital.Identity.Domain.Tenants;
using JadeCapital.Identity.Domain.Users;
using JadeCapital.Shared.Infrastructure.Email;
using JadeCapital.Shared.Kernel.MultiTenancy;
using JadeCapital.Shared.Kernel.Time;
using NSubstitute.ReturnsExtensions;

namespace JadeCapital.Identity.UnitTests.Features.Tenants.InviteTenantUser;

/// <summary>
/// Tests for <see cref="InviteTenantUserHandler"/> (Wave 6, slice 6c.3).
///
/// <para>
/// Four RED scenarios pinned here (per tasks.md 6c.3 line 370):
/// </para>
/// <list type="number">
///   <item>Valid email → invite sent (mock <see cref="IEmailSender"/> captures the message)</item>
///   <item>Existing user with same email → assigned to the tenant (no email sent)</item>
///   <item>Cross-tenant invite → 404 <c>tenant.not_found</c></item>
///   <item>Tenant at capacity (Personal plan: 10 users) → 422 <c>tenant.at_capacity</c></item>
/// </list>
///
/// <para>
/// The invite handler uses <see cref="IEmailSender.SendTenantInviteAsync"/>
/// — a stub method added to the shared abstraction in 6c.3. The actual
/// transport composition (Spanish/Jade-branded mime) is left for a later
/// slice; the stub captures the message for assertions.
/// </para>
/// </summary>
public class InviteTenantUserHandlerTests
{
    private readonly ITenantRepository _tenants = Substitute.For<ITenantRepository>();
    private readonly IUserRepository _users = Substitute.For<IUserRepository>();
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();
    private readonly IClock _clock = Substitute.For<IClock>();
    private readonly ITenantContext _tenantContext = Substitute.For<ITenantContext>();
    private readonly IEmailSender _email = Substitute.For<IEmailSender>();

    private InviteTenantUserHandler CreateSut() => new(_tenants, _users, _uow, _clock, _tenantContext, _email);

    private static readonly DateTimeOffset FixedNow = new(2026, 8, 19, 12, 0, 0, TimeSpan.Zero);

    private static Tenant CreateActiveTenant(Guid ownerId, IClock clock, TenantPlan plan = TenantPlan.Personal)
        => Tenant.Create(Guid.NewGuid(), "Acme", "acme", ownerId, plan, clock).Value;

    [Fact]
    public async Task Handle_ValidEmail_SendsInvite()
    {
        var ownerId = Guid.NewGuid();
        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(FixedNow);
        var tenant = CreateActiveTenant(ownerId, clock);
        _tenants.GetByIdAsync(tenant.Id, Arg.Any<CancellationToken>()).Returns(tenant);
        _tenantContext.Current.Returns(new TenantId(tenant.Id));
        _tenantContext.IsSuperAdmin.Returns(false);
        _users.FindByEmailAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).ReturnsNull();
        _users.CountByTenantIdAsync(new TenantId(tenant.Id), Arg.Any<CancellationToken>()).Returns(1);
        _uow.SaveChangesAsync(Arg.Any<CancellationToken>()).Returns(Result.Success(1));

        var cmd = new InviteTenantUserCommand(tenant.Id, Email: "invitee@test.com", DisplayName: "Invitee");
        var result = await CreateSut().Handle(cmd, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().NotBe(Guid.Empty);
        await _email.Received(1).SendTenantInviteAsync(
            Arg.Is<TenantInviteEmailMessage>(m => m.To == "invitee@test.com"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ExistingUserEmail_AssignsToTenantAndSkipsEmail()
    {
        var ownerId = Guid.NewGuid();
        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(FixedNow);
        var tenant = CreateActiveTenant(ownerId, clock);
        _tenants.GetByIdAsync(tenant.Id, Arg.Any<CancellationToken>()).Returns(tenant);
        _tenantContext.Current.Returns(new TenantId(tenant.Id));
        _tenantContext.IsSuperAdmin.Returns(false);

        var existing = User.Register(Guid.NewGuid(), "existing@test.com", "Existing", "h", UserRole.Trader).Value;
        _users.FindByEmailAsync("existing@test.com", Arg.Any<CancellationToken>()).Returns(existing);
        _users.CountByTenantIdAsync(new TenantId(tenant.Id), Arg.Any<CancellationToken>()).Returns(1);
        _uow.SaveChangesAsync(Arg.Any<CancellationToken>()).Returns(Result.Success(1));

        var cmd = new InviteTenantUserCommand(tenant.Id, Email: "existing@test.com", DisplayName: null);
        var result = await CreateSut().Handle(cmd, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(existing.Id);
        await _email.DidNotReceive().SendTenantInviteAsync(
            Arg.Any<TenantInviteEmailMessage>(), Arg.Any<CancellationToken>());
        // The existing user should be assigned to the tenant.
        existing.TenantId.Should().NotBeNull();
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

        var cmd = new InviteTenantUserCommand(tenant.Id, Email: "x@y.com", DisplayName: null);
        var result = await CreateSut().Handle(cmd, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("notfound.tenant.not_found");
        await _email.DidNotReceive().SendTenantInviteAsync(
            Arg.Any<TenantInviteEmailMessage>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_TenantAtCapacity_ReturnsUnprocessable()
    {
        var ownerId = Guid.NewGuid();
        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(FixedNow);
        // Personal plan: capacity = 10. Fill it.
        var tenant = CreateActiveTenant(ownerId, clock, TenantPlan.Personal);
        _tenants.GetByIdAsync(tenant.Id, Arg.Any<CancellationToken>()).Returns(tenant);
        _tenantContext.Current.Returns(new TenantId(tenant.Id));
        _tenantContext.IsSuperAdmin.Returns(false);
        _users.CountByTenantIdAsync(new TenantId(tenant.Id), Arg.Any<CancellationToken>())
            .Returns(Tenant.MaxUsersForPlan(TenantPlan.Personal));

        var cmd = new InviteTenantUserCommand(tenant.Id, Email: "overflow@test.com", DisplayName: "Overflow");
        var result = await CreateSut().Handle(cmd, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("validation.tenant.at_capacity");
        await _email.DidNotReceive().SendTenantInviteAsync(
            Arg.Any<TenantInviteEmailMessage>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_EmailSendFailure_DoesNotRollBackUserCreation()
    {
        var ownerId = Guid.NewGuid();
        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(FixedNow);
        var tenant = CreateActiveTenant(ownerId, clock);
        _tenants.GetByIdAsync(tenant.Id, Arg.Any<CancellationToken>()).Returns(tenant);
        _tenantContext.Current.Returns(new TenantId(tenant.Id));
        _tenantContext.IsSuperAdmin.Returns(false);
        _users.FindByEmailAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).ReturnsNull();
        _users.CountByTenantIdAsync(new TenantId(tenant.Id), Arg.Any<CancellationToken>()).Returns(1);
        _uow.SaveChangesAsync(Arg.Any<CancellationToken>()).Returns(Result.Success(1));
        // SMTP transport throws — the user record MUST still be committed.
        _email.SendTenantInviteAsync(
            Arg.Any<TenantInviteEmailMessage>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException(new InvalidOperationException("SMTP simulated failure")));

        var cmd = new InviteTenantUserCommand(tenant.Id, Email: "flaky@test.com", DisplayName: "Flaky");
        var result = await CreateSut().Handle(cmd, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().NotBe(Guid.Empty);
        await _uow.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }
}
