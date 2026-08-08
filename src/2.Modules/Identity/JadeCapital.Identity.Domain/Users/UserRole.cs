namespace JadeCapital.Identity.Domain.Users;

/// <summary>
/// Roles de plataforma. El unico valor con permisos administrativos es Admin.
/// Trader es el rol por defecto para cualquier registro.
/// </summary>
public enum UserRole
{
    Trader = 1,
    Admin = 2
}