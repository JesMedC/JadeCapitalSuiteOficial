namespace JadeCapital.Identity.Domain.Users;

/// <summary>
/// Estados de cuenta. Determinan que operaciones puede hacer el usuario
/// y si puede autenticarse.
/// </summary>
public enum UserStatus
{
    /// <summary>Cuenta activa, puede autenticarse y operar.</summary>
    Active = 1,

    /// <summary>Cuenta suspendida temporalmente (admin o regla de negocio).</summary>
    Suspended = 2,

    /// <summary>Cuenta cancelada por el usuario.</summary>
    Cancelled = 3,

    /// <summary>Cuenta bloqueada por exceso de intentos fallidos de login.</summary>
    LockedOut = 4,

    /// <summary>
    /// Wave 10 slice 10.5 — GDPR Art. 17 cascade initiated. The user has
    /// called <c>DELETE /api/users/me</c>; soft-delete is propagating to
    /// every user-owned aggregate. The account is fully anonymized
    /// (email replaced, display name redacted) but still present in the DB.
    /// The 30-day grace period has NOT been scheduled yet — the
    /// orchestrator flips this state to <see cref="ScheduledHardDelete"/>
    /// after the cascade completes.
    /// </summary>
    SoftDeleted = 5,

    /// <summary>
    /// Wave 10 slice 10.5 — 30-day grace period scheduled. The user
    /// cannot authenticate (login attempts return 401). On day 31 the
    /// <c>HardDeleteSweepBackgroundService</c> physically purges the
    /// row across every module and writes one pseudonymized
    /// <c>audit.events</c> entry for the compliance trail.
    /// </summary>
    ScheduledHardDelete = 6,

    /// <summary>
    /// Wave 10 slice 10.5 — terminal state after hard-delete sweep. The
    /// <c>identity.users</c> row is gone; the entity is reachable only
    /// via the pseudonymized audit trail.
    /// </summary>
    HardDeleted = 7
}