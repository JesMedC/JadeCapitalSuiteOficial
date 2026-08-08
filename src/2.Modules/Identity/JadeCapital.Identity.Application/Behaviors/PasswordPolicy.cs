namespace JadeCapital.Identity.Application.Behaviors;

/// <summary>
/// Politicas de password. Constantes centralizadas para mantener consistencia entre
/// validadores de Application y las reglas de Infrastructure.
/// </summary>
public static class PasswordPolicy
{
    public const int MinLength = 10;
    public const int MaxLength = 128;
    public const int MinUniqueChars = 4;

    /// <summary>Valida complejidad minima: letras + digitos + simbolos opcionales.</summary>
    public static bool MeetsComplexity(string password)
    {
        if (string.IsNullOrEmpty(password)) return false;
        var hasLetter = password.Any(char.IsLetter);
        var hasDigit = password.Any(char.IsDigit);
        var uniqueChars = new HashSet<char>(password).Count;
        return hasLetter && hasDigit && uniqueChars >= MinUniqueChars;
    }
}