using JadeCapital.Identity.Application.Abstractions;
using JadeCapital.Identity.Application.Features.Auth.DeleteAccount;
using JadeCapital.Shared.Kernel.Audit;
using JadeCapital.Shared.Kernel.Time;
using NSubstitute.ReturnsExtensions;

namespace JadeCapital.Identity.UnitTests.Features.Auth;

// ============================================================================
//  DeleteAccountHandlerTests — Wave 11 slice 11.2b
//
//  Coverage (per tasks.md §11.2b Phase 1):
//    1.1 AnonymizesUserFields        — RED-first
//    1.2 InvokesOrchestrator         — RED-first
//    1.3 EmitsAuditRow               — RED-first
//    1.4 NonexistentUserReturns404   — RED-first
//    1.5 CascadeSoftDeleteFails      — RED-first
//  +  Handle_AuditLog_RecordsUserAnonymization
//
//  Strict TDD: every test was authored RED-first against a non-existent
//  handler. The handler implementation in
//  `JadeCapital.Identity.Application/Features/Auth/DeleteAccount/DeleteAccountHandler.cs`
//  was written to GREEN each scenario.
// ============================================================================

public class DeleteAccountHandlerTests
{
    private readonly IUserRepository _users = Substitute.For<IUserRepository>();
    private readonly IGdprCascadeOrchestrator _orchestrator = Substitute.For<IGdprCascadeOrchestrator>();
    private readonly IAuditLogger _audit = Substitute.For<IAuditLogger>();
    private readonly IClock _clock = Substitute.For<IClock>();
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();
    private readonly ILogger<DeleteAccountHandler> _logger = Substitute.For<ILogger<DeleteAccountHandler>>();

    public DeleteAccountHandlerTests()
    {
        var fixedNow = DateTimeOffset.UtcNow;
        _clock.UtcNow.Returns(fixedNow);
    }

    private DeleteAccountHandler CreateSut() => new(
        _users, _orchestrator, _audit, _clock, _uow, _logger);

    private static User CreateActiveUser(Guid? tenantId = null)
    {
        var tid = tenantId ?? Guid.NewGuid();
        var t = new JadeCapital.Shared.Kernel.MultiTenancy.TenantId(tid);
        var userResult = User.Register(
            Guid.NewGuid(),
            email: "user@example.com",
            displayName: "Test User",
            passwordHash: "hashed_pwd",
            role: UserRole.Trader,
            acceptedTermsVersion: "v1",
            acceptedPrivacyVersion: "v1");
        // Assign tenant so the cascade audit entry has a TenantId.
        userResult.Value.AssignToTenant(t);
        return userResult.Value;
    }

    // ============================================
    // Scenario 1.1: AnonymizesUserFields
    // ============================================

    [Fact]
    public async Task Handle_ValidUser_AnonymizesAndSchedulesHardDelete()
    {
        var user = CreateActiveUser();
        _users.FindByIdAsync(user.Id, Arg.Any<CancellationToken>()).Returns(user);
        _orchestrator.CascadeSoftDeleteAsync(user.Id, Arg.Any<CancellationToken>()).Returns(5);
        _uow.SaveChangesAsync(Arg.Any<CancellationToken>())
            .Returns(JadeCapital.Shared.Kernel.Results.Result.Success(1));

        var cmd = new DeleteAccountCommand(user.Id);
        var result = await CreateSut().Handle(cmd, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        user.Status.Should().Be(UserStatus.ScheduledHardDelete);
        user.SoftDeletedAt.Should().NotBeNull();
        user.ScheduledHardDeleteAt.Should().NotBeNull();
        user.ScheduledHardDeleteAt!.Value.Should().Be(
            user.SoftDeletedAt!.Value.AddDays(30),
            "the 30-day grace window is computed from SoftDeletedAt");
        user.Email.Should().StartWith("deleted-").And.EndWith("@anonymized.local");
        user.DisplayName.Should().Be("Deleted User");
        user.PasswordHash.Should().BeEmpty();
    }

    // ============================================
    // Scenario 1.2: InvokesOrchestrator
    // ============================================

    [Fact]
    public async Task Handle_ValidUser_InvokesOrchestratorAndPersists()
    {
        var user = CreateActiveUser();
        _users.FindByIdAsync(user.Id, Arg.Any<CancellationToken>()).Returns(user);
        _orchestrator.CascadeSoftDeleteAsync(user.Id, Arg.Any<CancellationToken>()).Returns(7);
        _uow.SaveChangesAsync(Arg.Any<CancellationToken>())
            .Returns(JadeCapital.Shared.Kernel.Results.Result.Success(1));

        var cmd = new DeleteAccountCommand(user.Id);
        var result = await CreateSut().Handle(cmd, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.CascadeSoftDeletedRows.Should().Be(7);
        await _orchestrator.Received(1).CascadeSoftDeleteAsync(user.Id, Arg.Any<CancellationToken>());
        await _uow.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    // ============================================
    // Scenario 1.3: EmitsAuditRow
    // ============================================

    [Fact]
    public async Task Handle_ValidUser_EmitsAuditRowWithCascadeRows()
    {
        var user = CreateActiveUser();
        _users.FindByIdAsync(user.Id, Arg.Any<CancellationToken>()).Returns(user);
        _orchestrator.CascadeSoftDeleteAsync(user.Id, Arg.Any<CancellationToken>()).Returns(12);
        _uow.SaveChangesAsync(Arg.Any<CancellationToken>())
            .Returns(JadeCapital.Shared.Kernel.Results.Result.Success(1));

        var cmd = new DeleteAccountCommand(user.Id);
        await CreateSut().Handle(cmd, CancellationToken.None);

        await _audit.Received(1).LogAsync(
            Arg.Is<AuditEventEntry>(e =>
                e.EntityType == "User"
                && e.EntityId == user.Id
                && e.Action == AuditAction.Deleted
                && e.UserId == user.Id
                && e.ChangesJson != null
                && e.ChangesJson.Contains("cascade_rows")),
            Arg.Any<CancellationToken>());
    }

    // ============================================
    // Scenario 1.4: NonexistentUserReturns404
    // ============================================

    [Fact]
    public async Task Handle_NonexistentUser_ReturnsNotFoundError()
    {
        var userId = Guid.NewGuid();
        _users.FindByIdAsync(userId, Arg.Any<CancellationToken>()).ReturnsNull();

        var cmd = new DeleteAccountCommand(userId);
        var result = await CreateSut().Handle(cmd, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().StartWith("notfound");
        await _orchestrator.DidNotReceiveWithAnyArgs().CascadeSoftDeleteAsync(default, default);
        await _audit.DidNotReceiveWithAnyArgs().LogAsync(default!, default);
        await _uow.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    // ============================================
    // Scenario 1.5: CascadeSoftDeleteFails
    // ============================================

    [Fact]
    public async Task Handle_CascadeSoftDelete_StillSucceeds_BestEffort()
    {
        // The orchestrator swallows per-deletor exceptions internally
        // (Wave 10.5 design contract), so a "failing" cascade still
        // returns the partial count. The handler succeeds as long as
        // the user-row mutation succeeded.
        var user = CreateActiveUser();
        _users.FindByIdAsync(user.Id, Arg.Any<CancellationToken>()).Returns(user);
        _orchestrator.CascadeSoftDeleteAsync(user.Id, Arg.Any<CancellationToken>()).Returns(2);
        _uow.SaveChangesAsync(Arg.Any<CancellationToken>())
            .Returns(JadeCapital.Shared.Kernel.Results.Result.Success(1));

        var cmd = new DeleteAccountCommand(user.Id);
        var result = await CreateSut().Handle(cmd, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.CascadeSoftDeletedRows.Should().Be(2);
    }

    // ============================================
    // Scenario: Audit log records user anonymization
    // ============================================

    [Fact]
    public async Task Handle_ValidUser_AuditEntryCarriesTenantIdAndReason()
    {
        var tenantId = Guid.NewGuid();
        var user = CreateActiveUser(tenantId);
        _users.FindByIdAsync(user.Id, Arg.Any<CancellationToken>()).Returns(user);
        _orchestrator.CascadeSoftDeleteAsync(user.Id, Arg.Any<CancellationToken>()).Returns(0);
        _uow.SaveChangesAsync(Arg.Any<CancellationToken>())
            .Returns(JadeCapital.Shared.Kernel.Results.Result.Success(1));

        var cmd = new DeleteAccountCommand(user.Id);
        await CreateSut().Handle(cmd, CancellationToken.None);

        await _audit.Received(1).LogAsync(
            Arg.Is<AuditEventEntry>(e =>
                e.TenantId == tenantId
                && e.OccurredAt == _clock.UtcNow
                && e.ChangesJson!.Contains("reason")),
            Arg.Any<CancellationToken>());
    }

    // ============================================
    // Scenario: Already-deleted user is rejected
    // ============================================

    [Fact]
    public async Task Handle_AlreadySoftDeleted_ReturnsConflict()
    {
        var user = CreateActiveUser();
        // First call to anonymize succeeds — second call must reject.
        user.AnonymizeForGdprDelete(_clock);
        _users.FindByIdAsync(user.Id, Arg.Any<CancellationToken>()).Returns(user);

        var cmd = new DeleteAccountCommand(user.Id);
        var result = await CreateSut().Handle(cmd, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().StartWith("conflict");
        await _orchestrator.DidNotReceiveWithAnyArgs().CascadeSoftDeleteAsync(default, default);
        await _uow.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }
}
