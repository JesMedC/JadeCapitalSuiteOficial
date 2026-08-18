using JadeCapital.Identity.Application.Abstractions;
using JadeCapital.Identity.Domain.Common;
using JadeCapital.Identity.Domain.Tenants;
using JadeCapital.Identity.Domain.Users;
using JadeCapital.Shared.Infrastructure.Email;
using JadeCapital.Shared.Kernel.MultiTenancy;
using JadeCapital.Shared.Kernel.Results;
using JadeCapital.Shared.Kernel.Time;
using MediatR;

namespace JadeCapital.Identity.Application.Features.Tenants.InviteTenantUser;

/// <summary>
/// Command to invite (or assign) a user to a tenant (Wave 6, slice 6c.3).
///
/// <para>
/// Two happy paths:
/// </para>
/// <list type="bullet">
///   <item><b>Existing user with the email</b> → assigned to the tenant
///         (no email sent; the user already has credentials).</item>
///   <item><b>Brand-new email</b> → a placeholder User is created (no
///         password; the invite email carries the activation link), and
///         <see cref="IEmailSender.SendTenantInviteAsync"/> is called.</item>
/// </list>
///
/// <para>
/// <b>Capacity rule</b> (per <see cref="Tenant.MaxUsersForPlan"/>):
/// Personal=10, Pro=100, Enterprise=1000. Saturated tenants → 422
/// <c>tenant.at_capacity</c> BEFORE the invite is created so the user
/// never gets a misleading email.
/// </para>
/// </summary>
public sealed record InviteTenantUserCommand(
    Guid TenantId,
    string Email,
    string? DisplayName) : IRequest<Result<Guid>>;

/// <summary>
/// Result returned by the handler: the user id that was assigned
/// (existing user) or created (new invitee). Clients use this id to
/// reference the membership in subsequent calls.
/// </summary>
public sealed record InviteTenantUserResult(Guid UserId, bool EmailSent);

/// <summary>
/// Handler that invites a user to a tenant (Wave 6, slice 6c.3).
///
/// <para>
/// <b>6c.3 rules</b>:
/// </para>
/// <list type="number">
///   <item>If the tenant doesn't exist → 404 <c>tenant.not_found</c>.</item>
///   <item>If the caller is not in the target tenant (and not SuperAdmin) → 404
///         <c>tenant.not_found</c>.</item>
///   <item>If the tenant is <see cref="TenantStatus.Suspended"/> → 422
///         <c>tenant.cannot_modify_suspended</c>.</item>
///   <item>If <c>users in tenant >= MaxUsersForPlan(plan)</c> → 422
///         <c>tenant.at_capacity</c> (BEFORE any invite work).</item>
///   <item>If a user with the email exists → assign to tenant (no email).</item>
///   <item>Otherwise → register a placeholder user + assign + send invite.</item>
/// </list>
///
/// <para>
/// <b>Email transport</b>: uses the shared
/// <see cref="IEmailSender.SendTenantInviteAsync"/> stub. Production
/// wires log a warning in 6c.3 (the Jade-branded invite template lands
/// in a follow-up slice); tests use the in-memory capture to assert.
/// </para>
/// </summary>
public sealed class InviteTenantUserHandler : IRequestHandler<InviteTenantUserCommand, Result<Guid>>
{
    /// <summary>
    /// Default expiry window for a tenant-invite email (24h). Past this
    /// window the invite token is invalid; the user has to be re-invited.
    /// Future slice will wire token revocation + audit; for now the
    /// expiry is enforced only at the email-receipt side (the link
    /// carries the timestamp).
    /// </summary>
    public static readonly TimeSpan InviteExpiry = TimeSpan.FromHours(24);

    private readonly ITenantRepository _tenants;
    private readonly IUserRepository _users;
    private readonly IUnitOfWork _uow;
    private readonly IClock _clock;
    private readonly ITenantContext _tenantContext;
    private readonly IEmailSender _email;

    public InviteTenantUserHandler(
        ITenantRepository tenants,
        IUserRepository users,
        IUnitOfWork uow,
        IClock clock,
        ITenantContext tenantContext,
        IEmailSender email)
    {
        _tenants = tenants;
        _users = users;
        _uow = uow;
        _clock = clock;
        _tenantContext = tenantContext;
        _email = email;
    }

    public async Task<Result<Guid>> Handle(InviteTenantUserCommand req, CancellationToken ct)
    {
        var tenant = await _tenants.GetByIdAsync(req.TenantId, ct);
        if (tenant is null)
            return Result.Failure<Guid>(TenantErrors.NotFound.TenantNotFound);

        // Cross-tenant access returns 404.
        var callerTenant = _tenantContext.Current;
        if (callerTenant is null)
            return Result.Failure<Guid>(TenantErrors.NotFound.CrossTenantAccess);
        if (callerTenant.Value != tenant.Id && !_tenantContext.IsSuperAdmin)
            return Result.Failure<Guid>(TenantErrors.NotFound.CrossTenantAccess);

        // Compliance-hold.
        var suspendedGuard = Tenant.GuardNotSuspendedForMutation(tenant.Status);
        if (suspendedGuard.IsFailure)
            return Result.Failure<Guid>(suspendedGuard.Error);

        // Capacity check — single COUNT query, cheap.
        var currentCount = await _users.CountByTenantIdAsync(new TenantId(tenant.Id), ct);
        var capacity = Tenant.MaxUsersForPlan(tenant.Plan);
        if (currentCount >= capacity)
            return Result.Failure<Guid>(TenantErrors.Capacity.AtCapacity);

        // Existing-user path: assign in place, no email.
        var normalizedEmail = req.Email.Trim().ToLowerInvariant();
        var existing = await _users.FindByEmailAsync(normalizedEmail, ct);
        if (existing is not null)
        {
            var assign = existing.AssignToTenant(new TenantId(tenant.Id));
            if (assign.IsFailure)
                return Result.Failure<Guid>(assign.Error);

            await _users.UpdateAsync(existing, ct);
            var saved = await _uow.SaveChangesAsync(ct);
            return saved.IsFailure
                ? Result.Failure<Guid>(saved.Error)
                : Result.Success(existing.Id);
        }

        // Brand-new user path: register a placeholder (no real password hash —
        // the invitee sets their own on first activation). The aggregate's
        // <see cref="User.Register"/> rejects an empty hash; we use a
        // sentinel constant that the login path rejects via the PBKDF2
        // verify (it never matches a real hash format) — the actual
        // activation flow ships in a follow-up slice.
        const string AwaitingActivationSentinel = "__AWAITING_ACTIVATION__";
        var newUserResult = User.Register(
            id: Guid.NewGuid(),
            email: normalizedEmail,
            displayName: req.DisplayName ?? normalizedEmail,
            passwordHash: AwaitingActivationSentinel,
            role: UserRole.Trader);

        if (newUserResult.IsFailure)
            return Result.Failure<Guid>(newUserResult.Error);

        var newUser = newUserResult.Value;
        var assignNew = newUser.AssignToTenant(new TenantId(tenant.Id));
        if (assignNew.IsFailure)
            return Result.Failure<Guid>(assignNew.Error);

        await _users.AddAsync(newUser, ct);
        var savedNew = await _uow.SaveChangesAsync(ct);
        if (savedNew.IsFailure)
            return Result.Failure<Guid>(savedNew.Error);

        // Send the invite email. Failures here are non-fatal (the user
        // record is committed; ops can re-send from the admin UI).
        var inviteToken = Guid.NewGuid();
        var expiresAt = _clock.UtcNow.Add(InviteExpiry);
        try
        {
            await _email.SendTenantInviteAsync(
                new TenantInviteEmailMessage(
                    To: normalizedEmail,
                    DisplayName: req.DisplayName,
                    TenantName: tenant.Name,
                    InvitationToken: inviteToken,
                    ExpiresAt: expiresAt),
                ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Swallow + log via the email sender's logger. The user
            // record stays; the invite can be re-sent. Domain failure
            // codes (validation, conflict) would have surfaced above.
            // No `IdentityDomainErrors.User.*` token here because email
            // failures are an infrastructure concern, not a domain one.
            _ = ex; // referenced for future logging hook; intentionally swallowed.
        }

        return Result.Success(newUser.Id);
    }
}
