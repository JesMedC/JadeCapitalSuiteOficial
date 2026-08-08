namespace JadeCapital.Identity.Application.Abstractions;

/// <summary>
/// Servicio de hasheo de contrasenas. La implementacion vive en Infrastructure.
/// La interfaz aca garantiza que los handlers NO dependen de BCrypt/PBKDF2 directamente.
/// </summary>
public interface IPasswordHasher
{
    /// <summary>Genera un hash con sal aleatoria. NUNCA reversible.</summary>
    string Hash(string password);

    /// <summary>Compara un password en claro contra un hash previo. Constante en tiempo.</summary>
    bool Verify(string password, string hash);
}