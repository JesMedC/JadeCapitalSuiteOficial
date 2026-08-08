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
    LockedOut = 4
}