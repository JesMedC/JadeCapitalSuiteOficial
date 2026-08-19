using JadeCapital.Identity.Application.Abstractions;
using JadeCapital.Identity.Domain.Common;
using JadeCapital.Identity.Domain.Users;
using JadeCapital.Shared.Kernel.Audit;
using JadeCapital.Shared.Kernel.Results;
using JadeCapital.Shared.Kernel.Time;
using MediatR;
using Microsoft.Extensions.Logging;

namespace JadeCapital.Identity.Application.Features.Auth.DeleteAccount;

/// <summary>
/// Wave 11 slice 11.2b — handler for <see cref="DeleteAccountCommand"/>.
///
/// <para>
/// Flow (GDPR Art. 17 + Wave 10.5 cascade contract):
/// </para>
/// <list type="number">
///   <item>Load the user; 404 if not found.</item>
///   <item>Call <see cref="User.AnonymizeForGdprDelete"/>: email is
///         rewritten to <c>deleted-{guid}@anonymized.local</c>, display
///         name → "Deleted User", password hash cleared,
///         <see cref="User.SoftDeletedAt"/> set.</item>
///   <item>Call <see cref="User.ScheduleHardDelete"/>: bumps the user
///         into <c>ScheduledHardDelete</c> and writes
///         <see cref="User.ScheduledHardDeleteAt"/> = SoftDeletedAt + 30d.
///         Idempotent — second invocation is a no-op.</item>
///   <item>Invoke <see cref="IGdprCascadeOrchestrator.CascadeSoftDeleteAsync"/>:
///         resolves every registered <see cref="IUserCascadeDeletor"/>
///         across modules (Identity, Trading, Billing) and soft-deletes
///         every row the user owns. Per-deletor failures are swallowed
///         inside the orchestrator.</item>
///   <item>Persist the user-row changes via
///         <see cref="IUnitOfWork.SaveChangesAsync"/>.</item>
///   <item>Write a single <see cref="AuditAction.Deleted"/> entry to
///         <c>audit.events</c> carrying the GDPR reason and the cascade
///         row count.</item>
/// </list>
///
/// <para>
/// <b>Why no validator</b>: the handler receives <c>UserId</c> from the
/// JWT-derived <c>ClaimTypes.NameIdentifier</c>. The endpoint already
/// rejects unauthenticated requests (401) and the JWT's claim has been
/// parsed to <see cref="Guid"/> before the command is built. Any other
/// validation lives on the user aggregate.
/// </para>
/// </summary>
public sealed class DeleteAccountHandler : IRequestHandler<DeleteAccountCommand, Result<DeleteAccountResult>>
{
    private const int HardDeleteGraceDays = 30;

    private readonly IUserRepository _users;
    private readonly IGdprCascadeOrchestrator _orchestrator;
    private readonly IAuditLogger _audit;
    private readonly IClock _clock;
    private readonly IUnitOfWork _uow;
    private readonly ILogger<DeleteAccountHandler> _logger;

    public DeleteAccountHandler(
        IUserRepository users,
        IGdprCascadeOrchestrator orchestrator,
        IAuditLogger audit,
        IClock clock,
        IUnitOfWork uow,
        ILogger<DeleteAccountHandler> logger)
    {
        _users = users;
        _orchestrator = orchestrator;
        _audit = audit;
        _clock = clock;
        _uow = uow;
        _logger = logger;
    }

    public async Task<Result<DeleteAccountResult>> Handle(DeleteAccountCommand req, CancellationToken ct)
    {
        var user = await _users.FindByIdAsync(req.UserId, ct);
        if (user is null)
        {
            return Result.Failure<DeleteAccountResult>(Error.NotFound(
                "identity.user_not_found",
                "User not found."));
        }

        // 1. Anonymize the user row (email → "deleted-<guid>@anonymized.local",
        //    display name → "Deleted User", password hash cleared,
        //    SoftDeletedAt set).
        var anonymizeResult = user.AnonymizeForGdprDelete(_clock);
        if (anonymizeResult.IsFailure)
        {
            _logger.LogWarning(
                "DeleteAccount: anonymization rejected for user {UserId}: {Code}",
                req.UserId, anonymizeResult.Error.Code);
            return Result.Failure<DeleteAccountResult>(anonymizeResult.Error);
        }

        // 2. Schedule hard delete 30 days after SoftDeletedAt.
        var scheduleResult = user.ScheduleHardDelete(_clock, TimeSpan.FromDays(HardDeleteGraceDays));
        if (scheduleResult.IsFailure)
        {
            _logger.LogWarning(
                "DeleteAccount: schedule hard delete rejected for user {UserId}: {Code}",
                req.UserId, scheduleResult.Error.Code);
            return Result.Failure<DeleteAccountResult>(scheduleResult.Error);
        }

        // 3. Persist the user-row mutations before the cascade so a
        //    cascade failure on another module does NOT roll back the
        //    user-row flip. The user's right to be forgotten is the
        //    primary invariant; cascade completeness is best-effort.
        await _users.UpdateAsync(user, ct);
        var saved = await _uow.SaveChangesAsync(ct);
        if (saved.IsFailure)
        {
            return Result.Failure<DeleteAccountResult>(saved.Error);
        }

        // 4. Run cascade soft-delete across Identity + Trading + Billing.
        //    Per-deletor exceptions are swallowed inside the orchestrator
        //    (Wave 10.5 contract); the returned int is the sum of rows
        //    touched across every deletor.
        var cascadeRows = await _orchestrator.CascadeSoftDeleteAsync(req.UserId, ct);

        // 5. Audit row for the GDPR anonymization. Fire-and-forget:
        //    IAuditLogger never throws (slice 6d.2 contract). The
        //    audit row records the reason + the cascade row count so
        //    compliance can trace which row fan-out was performed.
        var reason = string.IsNullOrWhiteSpace(req.Reason) ? "user_initiated" : req.Reason.Trim();
        var changesJson =
            $"{{\"reason\":\"{reason}\",\"cascade_rows\":{cascadeRows},\"grace_days\":{HardDeleteGraceDays}}}";

        await _audit.LogAsync(new AuditEventEntry(
            EntityType: "User",
            EntityId: req.UserId,
            Action: AuditAction.Deleted,
            TenantId: user.TenantId?.Value,
            UserId: req.UserId,
            ChangesJson: changesJson,
            OccurredAt: _clock.UtcNow), ct);

        _logger.LogInformation(
            "DeleteAccount: user {UserId} anonymized; cascade touched {Rows} row(s); hard delete scheduled at {ScheduledAt}.",
            req.UserId, cascadeRows, user.ScheduledHardDeleteAt);

        return Result.Success(new DeleteAccountResult(
            req.UserId,
            user.SoftDeletedAt!.Value,
            user.ScheduledHardDeleteAt!.Value,
            cascadeRows));
    }
}
