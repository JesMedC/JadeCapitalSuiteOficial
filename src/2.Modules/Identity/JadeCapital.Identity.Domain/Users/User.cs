using JadeCapital.Identity.Domain.Authentication;
using JadeCapital.Identity.Domain.Common;
using JadeCapital.Shared.Kernel.Primitives;
using JadeCapital.Shared.Kernel.Results;

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
    /// Incremented on every successful password change. Refresh tokens track the
    /// version they were issued under; a rotation revokes every pre-change token
    /// because their version no longer matches. Defaults to 0 for new users.
    /// </summary>
    public int SessionVersion { get; private set; }

    /// <summary>UTC offset preferida del usuario (IANA, p.ej. "America/Mexico_City"). Null = UTC.</summary>
    public string? Timezone { get; private set; }

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
}