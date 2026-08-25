using JadeCapital.Identity.Domain.Authentication;
using JadeCapital.Identity.Domain.Common;
using JadeCapital.Shared.Kernel.MultiTenancy;
using JadeCapital.Shared.Kernel.Primitives;
using JadeCapital.Shared.Kernel.Results;
using JadeCapital.Shared.Kernel.Time;

namespace JadeCapital.Identity.Domain.Users;

/// <summary>
/// Aggregate Root del bounded context Identity.
///
/// Representa una cuenta humana en la plataforma. NO contiene credenciales
/// en claro: solo el hash de la contraseña (responsabilidad de Infrastructure).
///
/// Reglas de negocio:
/// - Email unico, normalizado a lowercase.
/// - DisplayName publico, NO es username.
/// - Cambios de estado disparan eventos para auditoria.
/// - Lockout despues de N intentos fallidos.
/// </summary>
public sealed class User : AggregateRoot<Guid>
{
    public const int MaxFailedLoginAttempts = 5;
    public const int LockoutMinutes = 15;
    public static readonly Guid NonHumanSentinelId = Guid.Parse("00000000-0000-0000-0000-000000000002");

    /// <summary>Maximum number of prior passwords retained for the reuse check.</summary>
    public const int MaxPasswordHistoryEntries = 5;

    public string Email { get; private set; } = default!;
    public string DisplayName { get; private set; } = default!;
    public string PasswordHash { get; private set; } = default!;
    public UserRole Role { get; private set; }
    public UserStatus Status { get; private set; }
    public DateTimeOffset? EmailConfirmedAt { get; private set; }
    public DateTimeOffset? LastLoginAt { get; private set; }
    public int FailedLoginCount { get; private set; }
    public DateTimeOffset? LockedUntil { get; private set; }

    /// <summary>
    /// Wave 10 slice 10.5 — UTC offset at which the soft-delete cascade
    /// flipped the user into <see cref="UserStatus.SoftDeleted"/>. NULL
    /// until DELETE /api/users/me fires. Mirrors the IsDeleted soft-delete
    /// flag's "deleted_at_utc" semantic for the GDPR state machine.
    /// </summary>
    public DateTimeOffset? SoftDeletedAt { get; private set; }

    /// <summary>
    /// Wave 10 slice 10.5 — UTC offset at which the row is eligible for
    /// hard-delete by <c>HardDeleteSweepBackgroundService</c>. Default is
    /// 30 days after <see cref="SoftDeletedAt"/>. NULL until the cascade
    /// handler writes it.
    /// </summary>
    public DateTimeOffset? ScheduledHardDeleteAt { get; private set; }

    /// <summary>
    /// Wave 10 slice 10.5 — Terms-of-Service version the user accepted
    /// on registration. NULL for users created before slice 10.5 (no
    /// backfill — pre-existing users keep the NULL and are treated as
    /// "not yet accepted"; the GDPR DELETE endpoint is the only path
    /// that does NOT require re-acceptance for those accounts).
    /// </summary>
    public string? AcceptedTermsVersion { get; private set; }

    /// <summary>
    /// Wave 10 slice 10.5 — Privacy Policy version the user accepted
    /// on registration. Same NULL semantics as
    /// <see cref="AcceptedTermsVersion"/>.
    /// </summary>
    public string? AcceptedPrivacyVersion { get; private set; }

    /// <summary>
    /// Wave 10 slice 10.5 — UTC offset at which the user accepted ToS
    /// + Privacy Policy on signup. NULL for legacy accounts.
    /// </summary>
    public DateTimeOffset? AcceptedAt { get; private set; }

    /// <summary>
    /// Incremented on every successful password change. Refresh tokens track the
    /// version they were issued under; a rotation revokes every pre-change token
    /// because their version no longer matches. Defaults to 0 for new users.
    /// </summary>
    public int SessionVersion { get; private set; }

    /// <summary>UTC offset preferida del usuario (IANA, p.ej. "America/Mexico_City"). Null = UTC.</summary>
    public string? Timezone { get; private set; }

    /// <summary>
    /// Slice 4d — per-user attachment quota in bytes. Default 100 MiB
    /// (104857600) set by migration 0018. The admin can override via
    /// tier upgrade (deferred to a future slice); the value is read by
    /// <c>AttachmentQuotaEnforcer</c> via <c>IAttachmentQuotaReader</c>.
    /// </summary>
    public long AttachmentQuotaBytes { get; private set; }

    /// <summary>
    /// Slice 4d — cached aggregate of the user's uploaded attachment
    /// bytes. Maintained by ConfirmAttachmentUploadedHandler (incremental)
    /// and AttachmentLifecycleService (recomputed ground truth from
    /// SUM(trade_attachments.bytes) daily).
    /// </summary>
    public long AttachmentUsedBytes { get; private set; }

    /// <summary>
    /// Wave 6c — tenant membership. NULL pre-Wave-6 (slice 6c.1); NOT NULL
    /// after backfill (slice 6c.3). Stored in <c>identity.users.tenant_id</c>
    /// (migration 0025); the FK to <c>identity.tenants.id</c> is enforced at
    /// the DB level.
    /// </summary>
    public TenantId? TenantId { get; private set; }

    /// <summary>
    /// Wave 11 slice 11.4 — UTC offset at which the user accepted the Terms
    /// of Service during registration. NULL for legacy users (pre-slice
    /// 11.4) and for accounts created before consent tracking was
    /// enforced. Mirrors the slice-10.5 <see cref="AcceptedTermsVersion"/>
    /// but persists the TIMESTAMP rather than the version string. Stored
    /// at <c>identity.users.terms_accepted_at</c> (migration 0037).
    /// </summary>
    public DateTimeOffset? TermsAcceptedAt { get; private set; }

    /// <summary>
    /// Wave 11 slice 11.4 — UTC offset at which the user accepted the
    /// Privacy Policy during registration. See <see cref="TermsAcceptedAt"/>
    /// for the pre-existing version pointer. Stored at
    /// <c>identity.users.privacy_accepted_at</c> (migration 0037).
    /// </summary>
    public DateTimeOffset? PrivacyAcceptedAt { get; private set; }

    /// <summary>
    /// Wave 11 slice 11.4 — Client IP (IPv4 or IPv6) recorded at the time
    /// the user accepted the ToS + Privacy Policy. Persisted in the same
    /// wire as the consent timestamps so a DSAR intake can correlate the
    /// "when" with the "from where". Stored at
    /// <c>identity.users.consent_ip</c> (migration 0037).
    /// </summary>
    public string? ConsentIp { get; private set; }

    /// <summary>
    /// Wave 11 slice 11.3 — UTC offset at which the post-registration
    /// welcome email was sent. NULL until the registration handler fires
    /// the first successful send. The 7-day suppression window + the
    /// "send-once" contract live in <c>RegisterUserHandler</c>; this
    /// property is the persistent flag the handler reads/writes.
    /// Stored at <c>identity.users.welcome_email_sent_at</c>
    /// (migration 0036).
    /// </summary>
    public DateTimeOffset? WelcomeEmailSentAt { get; private set; }

    /// <summary>
    /// Wave 11 slice 11.4 — UTC offset at which the user last interacted
    /// with the cookie banner. NULL until the first banner interaction.
    /// Re-written on every subsequent choice change. Stored at
    /// <c>identity.users.cookie_consent_accepted_at</c> (migration 0038).
    /// </summary>
    public DateTimeOffset? CookieConsentAcceptedAt { get; private set; }

    /// <summary>
    /// Wave 11 slice 11.4 — Cookie tier chosen by the user ('all' or
    /// 'essential'). NULL until the first banner interaction. Stored at
    /// <c>identity.users.cookie_consent_choice</c> (migration 0038). The
    /// application-layer validator enforces the canonical set; this
    /// column accepts the raw string to keep the schema forward-
    /// compatible if the taxonomy grows.
    /// </summary>
    public string? CookieConsentChoice { get; private set; }

    /// <summary>
    /// Ordered (changed_at DESC, id DESC) list of the user's previous
    /// password hashes. Backing storage is the EF-mapped list; the public
    /// surface returns the newest-first projection.
    /// </summary>
    private readonly List<PasswordHistoryEntry> _passwordHistory = new();
    public IReadOnlyList<PasswordHistoryEntry> PasswordHistory
        => PasswordHistoryEntry.OrderNewestFirst(_passwordHistory);

    // EF Core.
    private User() { }

    private User(
        Guid id,
        string email,
        string displayName,
        string passwordHash,
        UserRole role) : base(id)
    {
        Email = email;
        DisplayName = displayName;
        PasswordHash = passwordHash;
        Role = role;
        Status = UserStatus.Active;
        EmailConfirmedAt = DateTimeOffset.UtcNow;
        AttachmentQuotaBytes = 104857600;
    }

    /// <summary>
    /// Crea un nuevo User. NO verifica unicidad de email (eso es Infrastructure
    /// antes de llamar aqui). El caller debe garantizar que el email no existe.
    /// </summary>
    public static Result<User> Register(
        Guid id,
        string email,
        string displayName,
        string passwordHash,
        UserRole role)
    {
        if (id == Guid.Empty)
            return Result.Failure<User>(IdentityDomainErrors.User.IdRequired);

        if (string.IsNullOrWhiteSpace(email)
            || !email.Contains('@')
            || email.StartsWith('@')
            || email.EndsWith('@')
            || email.IndexOf('.', email.IndexOf('@')) == -1)
            return Result.Failure<User>(IdentityDomainErrors.User.EmailInvalid);

        if (string.IsNullOrWhiteSpace(displayName) || displayName.Trim().Length < 2)
            return Result.Failure<User>(IdentityDomainErrors.User.DisplayNameInvalid);

        if (string.IsNullOrWhiteSpace(passwordHash))
            return Result.Failure<User>(IdentityDomainErrors.User.PasswordHashRequired);

        var user = new User(id, email.Trim().ToLowerInvariant(), displayName.Trim(), passwordHash, role);
        user.RaiseDomainEvent(new UserRegisteredDomainEvent(user.Id, user.Email, user.Role, DateTimeOffset.UtcNow));
        return Result.Success(user);
    }

    /// <summary>
    /// Wave 10 slice 10.5 — Crea un nuevo User con consentimiento explicito
    /// de Terms of Service + Privacy Policy (GDPR Art. 6 + ePrivacy
    /// directive). El handler de Register exige ambos flags antes de
    /// invocar esta factory; las versiones aceptadas se persisten en el
    /// agregado + el evento <see cref="UserRegisteredDomainEvent"/> lleva
    /// las versiones para audit.
    /// </summary>
    public static Result<User> Register(
        Guid id,
        string email,
        string displayName,
        string passwordHash,
        UserRole role,
        string acceptedTermsVersion,
        string acceptedPrivacyVersion)
    {
        if (string.IsNullOrWhiteSpace(acceptedTermsVersion))
            return Result.Failure<User>(IdentityDomainErrors.User.AcceptedTermsVersionRequired);

        if (string.IsNullOrWhiteSpace(acceptedPrivacyVersion))
            return Result.Failure<User>(IdentityDomainErrors.User.AcceptedPrivacyVersionRequired);

        var baseResult = Register(id, email, displayName, passwordHash, role);
        if (baseResult.IsFailure) return baseResult;

        var user = baseResult.Value;
        var now = DateTimeOffset.UtcNow;
        user.AcceptedTermsVersion = acceptedTermsVersion.Trim();
        user.AcceptedPrivacyVersion = acceptedPrivacyVersion.Trim();
        user.AcceptedAt = now;
        user.Touch();
        return Result.Success(user);
    }

    /// <summary>
    /// Wave 10 slice 10.5 — GDPR Art. 17 (right to be forgotten). Flips
    /// the user into <see cref="UserStatus.SoftDeleted"/>, anonymizes the
    /// email + display name, and records the <see cref="SoftDeletedAt"/>
    /// timestamp. The canonical cascade is:
    /// Active -> SoftDeleted -> ScheduledHardDelete -> HardDeleted
    /// (3 separate transitions, 3 separate domain methods).
    ///
    /// <para>
    /// <b>Why an explicit method instead of <c>User.Cancel(reason)</c></b>:
    /// Cancel is the soft-disabling path (status -> Cancelled, retention
    /// keeps the row for analytics). GDPR delete is the
    /// physical-purge-with-grace path. Different intent, different column
    /// fan-out (Cancel does NOT touch <see cref="ScheduledHardDeleteAt"/>).
    /// </para>
    /// </summary>
    public Result AnonymizeForGdprDelete(IClock clock)
    {
        if (clock is null) return Result.Failure(IdentityDomainErrors.User.ClockRequired);
        if (Status == UserStatus.HardDeleted)
            return Result.Failure(IdentityDomainErrors.User.AlreadyHardDeleted);

        if (Status == UserStatus.SoftDeleted || Status == UserStatus.ScheduledHardDelete)
            return Result.Failure(IdentityDomainErrors.User.AlreadyDeleted);

        var now = clock.UtcNow;
        Status = UserStatus.SoftDeleted;
        SoftDeletedAt = now;
        Email = $"deleted-{Id:N}@anonymized.local";
        DisplayName = "Deleted User";
        PasswordHash = string.Empty;
        FailedLoginCount = 0;
        LockedUntil = null;
        SessionVersion++;
        Touch();
        RaiseDomainEvent(new UserAnonymizedDomainEvent(Id, now));
        return Result.Success();
    }

    /// <summary>
    /// Wave 10 slice 10.5 — Schedules the hard-delete sweep 30 days (default)
    /// after <see cref="SoftDeletedAt"/>. Called by the GDPR cascade
    /// orchestrator after the soft-delete cascade succeeds. Idempotent:
    /// scheduling twice with the same grace does NOT bump the
    /// <see cref="ScheduledHardDeleteAt"/> (avoids re-extending the grace
    /// window on accidental double-invocation).
    /// </summary>
    public Result ScheduleHardDelete(IClock clock, TimeSpan? gracePeriod = null)
    {
        if (clock is null) return Result.Failure(IdentityDomainErrors.User.ClockRequired);

        if (Status != UserStatus.SoftDeleted)
            return Result.Failure(IdentityDomainErrors.User.NotSoftDeleted);

        if (ScheduledHardDeleteAt is not null)
            return Result.Success(); // idempotent no-op

        var grace = gracePeriod ?? TimeSpan.FromDays(30);
        var baseline = SoftDeletedAt ?? clock.UtcNow;
        ScheduledHardDeleteAt = baseline.Add(grace);
        Status = UserStatus.ScheduledHardDelete;
        Touch();
        RaiseDomainEvent(new UserScheduledHardDeleteDomainEvent(Id, ScheduledHardDeleteAt.Value, clock.UtcNow));
        return Result.Success();
    }

    /// <summary>
    /// Wave 10 slice 10.5 — Marks the user as hard-deleted (terminal). The
    /// actual row DELETE is performed by
    /// <c>HardDeleteSweepBackgroundService</c>; this method only flips the
    /// status so the audit trail knows the user is gone before the SQL
    /// DELETE fires. In practice the sweep deletes the row in the same
    /// transaction that flips the status — the method is kept for unit
    /// tests that exercise the state-machine invariants in isolation.
    /// </summary>
    public Result MarkHardDeleted(IClock clock)
    {
        if (clock is null) return Result.Failure(IdentityDomainErrors.User.ClockRequired);

        if (Status != UserStatus.ScheduledHardDelete)
            return Result.Failure(IdentityDomainErrors.User.NotScheduledForHardDelete);

        Status = UserStatus.HardDeleted;
        Touch();
        RaiseDomainEvent(new UserHardDeletedDomainEvent(Id, clock.UtcNow));
        return Result.Success();
    }

    public Result ChangePassword(string newPasswordHash)
    {
        if (string.IsNullOrWhiteSpace(newPasswordHash))
            return Result.Failure(IdentityDomainErrors.User.PasswordHashRequired);

        PasswordHash = newPasswordHash;
        Touch();
        RaiseDomainEvent(new UserPasswordChangedDomainEvent(Id, DateTimeOffset.UtcNow));
        return Result.Success();
    }

    /// <summary>
    /// Rotates the password atomically:
    /// 1. Refuses empty/whitespace hashes (structural validation).
    /// 2. Normalizes the backing history to (changed_at DESC, id DESC)
    ///    BEFORE prepend/evict — protects against EF hydration order corrupting
    ///    which entry gets evicted.
    /// 3. Displaces the current hash into the history (newest first),
    ///    evicts entries beyond <see cref="MaxPasswordHistoryEntries"/>,
    ///    assigns the new hash, and bumps <see cref="SessionVersion"/>.
    ///
    /// Plaintext-vs-stored-hash reuse detection does NOT live here. Salted
    /// PBKDF2 means the same plaintext yields a different encoded hash on
    /// every call; that responsibility belongs to the Application boundary
    /// (see <c>JadeCapital.Identity.Application.Authentication.PasswordChangeReuseChecker</c>).
    /// Failure leaves every field untouched.
    /// </summary>
    public Result ChangePasswordPreservingHistory(string newPasswordHash)
    {
        if (string.IsNullOrWhiteSpace(newPasswordHash))
            return Result.Failure(IdentityDomainErrors.User.PasswordHashRequired);

        var now = DateTimeOffset.UtcNow;

        // Normalize before mutation — the backing list may have been hydrated
        // in arbitrary order (EF without the descending index, or tests
        // injecting via reflection). Sorting first guarantees that eviction
        // removes the chronologically-oldest entry, not whichever entry
        // happens to sit at the tail of the unordered backing list.
        var normalized = PasswordHistoryEntry.OrderNewestFirst(_passwordHistory).ToList();

        var displaced = PasswordHistoryEntry.Create(Guid.NewGuid(), Id, PasswordHash, now);
        normalized.Insert(0, displaced);

        if (normalized.Count > MaxPasswordHistoryEntries)
        {
            normalized.RemoveRange(MaxPasswordHistoryEntries, normalized.Count - MaxPasswordHistoryEntries);
        }

        _passwordHistory.Clear();
        _passwordHistory.AddRange(normalized);

        PasswordHash = newPasswordHash;
        SessionVersion++;
        Touch();
        RaiseDomainEvent(new UserPasswordChangedDomainEvent(Id, now));
        return Result.Success();
    }

    /// <summary>
    /// Replaces the backing password-history list wholesale. Used by EF
    /// hydration (the framework populates the private field directly via the
    /// configured backing field access) and by trusted test fixtures that
    /// need to simulate hydration in arbitrary order. Production domain
    /// handlers MUST NOT call this — they go through
    /// <see cref="ChangePasswordPreservingHistory"/> so history invariants
    /// (5-newest retention, monotonic SessionVersion) are preserved.
    /// </summary>
    public void HydrateHistoryForTrusted(IEnumerable<PasswordHistoryEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        _passwordHistory.Clear();
        _passwordHistory.AddRange(entries);
    }

    public Result ChangeDisplayName(string newDisplayName)
    {
        if (string.IsNullOrWhiteSpace(newDisplayName) || newDisplayName.Trim().Length < 2)
            return Result.Failure(IdentityDomainErrors.User.DisplayNameInvalid);

        DisplayName = newDisplayName.Trim();
        Touch();
        return Result.Success();
    }

    public Result ChangeTimezone(string? timezone)
    {
        // Si es null se permite (vuelve a UTC). Si viene valor, validar formato basico.
        if (timezone is not null && (string.IsNullOrWhiteSpace(timezone) || timezone.Length > 64))
            return Result.Failure(IdentityDomainErrors.User.TimezoneInvalid);

        Timezone = string.IsNullOrWhiteSpace(timezone) ? null : timezone;
        Touch();
        return Result.Success();
    }

    /// <summary>
    /// Wave 11 slice 11.4 — Records GDPR Art. 7 consent capture at
    /// registration time. The caller (<c>RegisterUserHandler</c>) must
    /// have already validated the user clicked both checkboxes; this
    /// method only persists the timestamps + IP. Idempotent: re-calling
    /// does NOT bump <see cref="TermsAcceptedAt"/> if the column is
    /// already populated (preserves audit immutability).
    /// </summary>
    public Result RecordConsent(DateTimeOffset termsAcceptedAt, DateTimeOffset privacyAcceptedAt, string consentIp)
    {
        if (string.IsNullOrWhiteSpace(consentIp) || consentIp.Length > 45)
            return Result.Failure(IdentityDomainErrors.User.ConsentIpInvalid);

        // First-write wins on the timestamps. This protects the audit
        // trail from inadvertent double-calls (a future "resend"
        // endpoint, a typo in a test) silently bumping the legal record.
        if (TermsAcceptedAt is null) TermsAcceptedAt = termsAcceptedAt;
        if (PrivacyAcceptedAt is null) PrivacyAcceptedAt = privacyAcceptedAt;
        ConsentIp = consentIp.Trim();
        Touch();
        return Result.Success();
    }

    /// <summary>
    /// Wave 11 slice 11.4 — Persists the user's cookie-banner choice. The
    /// caller (<c>ConsentHandler</c>) enforces the canonical choice set
    /// at the application boundary; this method only writes the row.
    /// Idempotent: re-calling with the same choice does NOT bump
    /// <see cref="CookieConsentAcceptedAt"/> (avoids audit churn from
    /// background services that re-emit the banner).
    /// </summary>
    public Result RecordCookieConsent(DateTimeOffset acceptedAt, string choice)
    {
        if (string.IsNullOrWhiteSpace(choice) || choice.Length > 16)
            return Result.Failure(IdentityDomainErrors.User.CookieConsentChoiceInvalid);

        var trimmed = choice.Trim();
        if (CookieConsentChoice == trimmed)
        {
            // Same choice — keep the original timestamp (audit immutability).
            return Result.Success();
        }

        CookieConsentChoice = trimmed;
        CookieConsentAcceptedAt = acceptedAt;
        Touch();
        return Result.Success();
    }

    /// <summary>
    /// Wave 11 slice 11.3/11.4 — Marks that the post-registration welcome
    /// email was successfully sent. The 7-day idempotency window lives in
    /// the handler (the handler reads the column and decides whether to
    /// re-fire), this method only writes the timestamp.
    /// </summary>
    public void MarkWelcomeEmailSent(DateTimeOffset sentAt)
    {
        WelcomeEmailSentAt = sentAt;
        Touch();
    }

    public Result ChangeRole(UserRole newRole)
    {
        if (Role == newRole) return Result.Success();

        var previous = Role;
        Role = newRole;
        Touch();
        RaiseDomainEvent(new UserRoleChangedDomainEvent(Id, previous, newRole, DateTimeOffset.UtcNow));
        return Result.Success();
    }

    public Result Suspend(string reason)
    {
        if (Status == UserStatus.Cancelled)
            return Result.Failure(IdentityDomainErrors.User.CannotSuspendCancelled);

        if (string.IsNullOrWhiteSpace(reason))
            return Result.Failure(IdentityDomainErrors.User.SuspensionReasonRequired);

        Status = UserStatus.Suspended;
        Touch();
        RaiseDomainEvent(new UserSuspendedDomainEvent(Id, reason, DateTimeOffset.UtcNow));
        return Result.Success();
    }

    public Result Reactivate()
    {
        if (Status == UserStatus.Cancelled)
            return Result.Failure(IdentityDomainErrors.User.CannotReactivateCancelled);

        if (Status != UserStatus.Suspended && Status != UserStatus.LockedOut)
            return Result.Failure(IdentityDomainErrors.User.NotSuspended);

        Status = UserStatus.Active;
        FailedLoginCount = 0;
        LockedUntil = null;
        Touch();
        RaiseDomainEvent(new UserReactivatedDomainEvent(Id, DateTimeOffset.UtcNow));
        return Result.Success();
    }

    public Result Cancel()
    {
        if (Status == UserStatus.Cancelled)
            return Result.Failure(IdentityDomainErrors.User.AlreadyCancelled);

        Status = UserStatus.Cancelled;
        FailedLoginCount = 0;
        LockedUntil = null;
        Touch();
        RaiseDomainEvent(new UserCancelledDomainEvent(Id, DateTimeOffset.UtcNow));
        return Result.Success();
    }

    // ============================================
    // Login tracking
    // ============================================

    public Result RecordSuccessfulLogin()
    {
        if (Status == UserStatus.Cancelled)
            return Result.Failure(IdentityDomainErrors.User.CancelledCannotLogin);

        if (Status == UserStatus.Suspended)
            return Result.Failure(IdentityDomainErrors.User.SuspendedCannotLogin);

        if (LockedUntil is not null && LockedUntil > DateTimeOffset.UtcNow)
            return Result.Failure(IdentityDomainErrors.User.LockedOutCannotLogin);

        if (Status != UserStatus.Active)
            return Result.Failure(IdentityDomainErrors.User.NotActiveCannotLogin);

        FailedLoginCount = 0;
        LockedUntil = null;
        LastLoginAt = DateTimeOffset.UtcNow;
        Touch();
        return Result.Success();
    }

    public Result RecordFailedLogin()
    {
        if (Status != UserStatus.Active)
            // Solo cuentas activas pueden intentar login.
            return Result.Failure(IdentityDomainErrors.User.NotActiveCannotLogin);

        FailedLoginCount++;
        Touch();

        if (FailedLoginCount >= MaxFailedLoginAttempts)
        {
            Status = UserStatus.LockedOut;
            LockedUntil = DateTimeOffset.UtcNow.AddMinutes(LockoutMinutes);
            RaiseDomainEvent(new UserLockedOutDomainEvent(Id, LockedUntil.Value, DateTimeOffset.UtcNow));
        }

        return Result.Success();
    }

    public bool IsLockedOut(DateTimeOffset utcNow)
        => LockedUntil.HasValue && LockedUntil.Value > utcNow;

    public bool CanAuthenticate()
        => Status == UserStatus.Active
           && (LockedUntil is null || LockedUntil <= DateTimeOffset.UtcNow);

    public bool CanRecover(DateTimeOffset utcNow) => Id != NonHumanSentinelId && TenantId is not null
        && Status == UserStatus.Active && (LockedUntil is null || LockedUntil <= utcNow);

    // ============================================
    // Wave 6c.1 — Tenant membership
    // ============================================

    /// <summary>
    /// Assigns the user to a tenant. Idempotent on the same
    /// <see cref="TenantId"/>; cross-tenant re-assignment requires the
    /// Admin role.
    ///
    /// <para>
    /// <b>Slice 6c.1 contract</b>: the column is nullable (per the
    /// "ONE migration atómica" user decision — 6c.1=nullable,
    /// 6c.2=backfill, 6c.3=NOT NULL). Until 6c.2 runs, every user has
    /// <see cref="TenantId"/> = <c>null</c> and the first call sets it.
    /// </para>
    /// </summary>
    public Result AssignToTenant(TenantId tenantId)
    {
        if (tenantId.Value == Guid.Empty)
            return Result.Failure(IdentityDomainErrors.User.TenantIdInvalid);

        // Re-assign to the same tenant → idempotent no-op (no UpdatedAt bump).
        if (TenantId is not null && TenantId == tenantId)
            return Result.Success();

        // Cross-tenant re-assignment → admin-only.
        if (TenantId is not null && Role != UserRole.Admin)
            return Result.Failure(IdentityDomainErrors.User.CrossTenantReassignRequiresAdmin);

        TenantId = tenantId;
        Touch();
        return Result.Success();
    }

    /// <summary>
    /// Slice 6c.3 — unassigns the user from their current tenant. Used by
    /// <c>RemoveTenantUserHandler</c> for the "remove from workspace"
    /// semantics; the user account itself is NOT deleted. Sets
    /// <see cref="TenantId"/> back to <c>null</c> and bumps
    /// <see cref="Entity{T}.UpdatedAt"/>.
    ///
    /// <para>
    /// <b>Note</b>: this conflicts with the 6c.3 NOT NULL constraint on
    /// <c>identity.users.tenant_id</c>. The slice is safe ONLY because
    /// <c>RemoveTenantUserHandler</c> runs this from the tenant context
    /// after a re-assignment to a Personal default — i.e. the user is
    /// never left with <c>NULL</c>; they fall back to the Personal
    /// tenant assigned by the 6c.2 backfill. The 6c.3 handler
    /// implementation is responsible for that fallback. This method is
    /// the "explicit unassign" primitive; the handler decides whether
    /// it's safe to call.
    /// </para>
    /// </summary>
    public Result UnassignFromTenant()
    {
        if (TenantId is null)
            return Result.Success();   // already unassigned → idempotent no-op

        TenantId = null;
        Touch();
        return Result.Success();
    }

    /// <summary>
    /// Slice 6c.3 — admin-orchestrated reassignment of the user to a new
    /// tenant. Used by <c>RemoveTenantUserHandler</c> to remove a user
    /// from a workspace WITHOUT violating the NOT NULL constraint on
    /// <c>identity.users.tenant_id</c>: the user is reassigned to the
    /// Personal default tenant instead of leaving the column NULL.
    ///
    /// <para>
    /// <b>Why this is NOT <see cref="AssignToTenant"/></b>: that method
    /// guards against self-initiated cross-tenant reassignment
    /// (<c>Role != Admin → failure</c>). The remove-from-workspace flow
    /// is initiated by the TENANT OWNER on behalf of a target member —
    /// the target is not the actor. The actor-vs-subject split means we
    /// need a separate primitive that does NOT enforce the self-init
    /// guard. The handler (not the aggregate) is responsible for the
    /// authorization check (caller is tenant-owner or SuperAdmin).
    /// </para>
    ///
    /// <para>
    /// Idempotent on no-op: assigning to the current tenant returns
    /// <c>Success</c> without bumping <see cref="Entity{T}.UpdatedAt"/>.
    /// </para>
    /// </summary>
    public Result ReassignToTenantByAdmin(TenantId newTenantId)
    {
        if (newTenantId.Value == Guid.Empty)
            return Result.Failure(IdentityDomainErrors.User.TenantIdInvalid);

        // No-op when already in the target tenant — avoids spurious UpdatedAt bumps.
        if (TenantId is not null && TenantId.Value == newTenantId.Value)
            return Result.Success();

        TenantId = newTenantId;
        Touch();
        return Result.Success();
    }
}
