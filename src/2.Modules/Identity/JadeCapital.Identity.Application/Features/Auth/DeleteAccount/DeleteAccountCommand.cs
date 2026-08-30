using JadeCapital.Shared.Kernel.Results;
using MediatR;

namespace JadeCapital.Identity.Application.Features.Auth.DeleteAccount;

/// <summary>
/// Wave 11 slice 11.2b — GDPR Art. 17 (right to be forgotten) command.
///
/// <para>
/// Issued by <c>DELETE /api/users/me</c> after the JWT-derived userId is
/// validated. The handler anonymizes the user, schedules the 30-day
/// hard-delete sweep, runs the cross-module soft-delete cascade, and
/// writes an audit entry.
/// </para>
///
/// <para>
/// <b>Why a single command (not 2-3 separate ones)</b>: the GDPR
/// transition is atomic from the user's perspective — the request flips
/// the user into the cascade and the 30-day grace window starts now.
/// Splitting would let a partial state linger where the user is
/// anonymized but not cascaded (or vice versa), creating a compliance
/// gap. The single command + handler keeps the invariant tight.
/// </para>
/// </summary>
public sealed record DeleteAccountCommand(
    Guid UserId,
    string? Reason = null) : IRequest<Result<DeleteAccountResult>>;

/// <summary>
/// Response body for <c>DELETE /api/users/me</c>. Returned as
/// <see cref="Microsoft.AspNetCore.Http.HttpStatusCode.Accepted"/> so
/// the FE can show a confirmation toast and the 30-day countdown.
/// </summary>
public sealed record DeleteAccountResult(
    Guid UserId,
    DateTimeOffset SoftDeletedAt,
    DateTimeOffset ScheduledHardDeleteAt,
    int CascadeSoftDeletedRows,
    string Status = "SoftDeleted");
