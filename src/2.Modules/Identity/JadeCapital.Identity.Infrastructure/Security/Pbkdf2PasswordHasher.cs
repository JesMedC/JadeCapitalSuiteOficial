using JadeCapital.Identity.Application.Abstractions;
using Microsoft.AspNetCore.Cryptography.KeyDerivation;
using System.Security.Cryptography;

namespace JadeCapital.Identity.Infrastructure.Security;

/// <summary>
/// PBKDF2 password hasher. Parametros:
/// - Algoritmo: HMAC-SHA256.
/// - Iteraciones: 100,000 (NIST SP 800-132).
/// - Sal: 128 bits aleatorios por password.
/// - Salida: 256 bits.
/// - Formato: {iter}.{salB64}.{hashB64}.
/// </summary>
public sealed class Pbkdf2PasswordHasher : IPasswordHasher
{
    private const int SaltSize = 16;
    private const int HashSize = 32;
    private const KeyDerivationPrf Prf = KeyDerivationPrf.HMACSHA256;

    private readonly int _iterations;

    public Pbkdf2PasswordHasher(int iterations = 100_000)
    {
        if (iterations < 50_000)
            throw new ArgumentOutOfRangeException(nameof(iterations), "Iterations too low for prod.");
        _iterations = iterations;
    }

    public string Hash(string password)
    {
        if (string.IsNullOrEmpty(password))
            throw new ArgumentException("Password cannot be empty.", nameof(password));

        var salt = new byte[SaltSize];
        RandomNumberGenerator.Fill(salt);
        var hash = KeyDerivation.Pbkdf2(password, salt, Prf, _iterations, HashSize);
        return $"{_iterations}.{Convert.ToBase64String(salt)}.{Convert.ToBase64String(hash)}";
    }

    public bool Verify(string password, string stored)
    {
        if (string.IsNullOrEmpty(password) || string.IsNullOrEmpty(stored)) return false;

        var parts = stored.Split('.', 3);
        if (parts.Length != 3) return false;
        if (!int.TryParse(parts[0], out var iterations)) return false;

        try
        {
            var salt = Convert.FromBase64String(parts[1]);
            var expected = Convert.FromBase64String(parts[2]);
            var actual = KeyDerivation.Pbkdf2(password, salt, Prf, iterations, expected.Length);
            return CryptographicOperations.FixedTimeEquals(actual, expected);
        }
        catch
        {
            return false;
        }
    }
}