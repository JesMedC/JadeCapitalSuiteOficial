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

    public string Email { get; private set; } = default!;
    public string DisplayName { get; private set; } = default!;
    public string PasswordHash { get; private set; } = default!;
    public UserRole Role { get; private set; }
    public UserStatus Status { get; private set; }
    public DateTimeOffset? EmailConfirmedAt { get; private set; }
    public DateTimeOffset? LastLoginAt { get; private set; }
    public int FailedLoginCount { get; private set; }
    public DateTimeOffset? LockedUntil { get; private set; }

    /// <summary>UTC offset preferida del usuario (IANA, p.ej. "America/Mexico_City"). Null = UTC.</summary>
    public string? Timezone { get; private set; }

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