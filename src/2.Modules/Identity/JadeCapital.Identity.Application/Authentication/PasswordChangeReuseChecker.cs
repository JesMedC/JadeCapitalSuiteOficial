using JadeCapital.Identity.Application.Abstractions;
using JadeCapital.Identity.Domain.Users;

namespace JadeCapital.Identity.Application.Authentication;

/// <summary>
/// Default <see cref="IPasswordChangeReuseChecker"/>. Iterates the user's
/// CURRENT hash + each of the retained PRIOR five hashes and asks
/// <see cref="IPasswordHasher.Verify"/> to confirm. The hasher handles salt
/// extraction and constant-time comparison, so this class stays
/// format-agnostic — only PBKDF2 today, but swap the hasher to switch schemes.
/// </summary>
public sealed class PasswordChangeReuseChecker : IPasswordChangeReuseChecker
{
    private readonly IPasswordHasher _hasher;

    public PasswordChangeReuseChecker(IPasswordHasher hasher)
    {
        ArgumentNullException.ThrowIfNull(hasher);
        _hasher = hasher;
    }

    public bool IsReused(User user, string newPasswordPlaintext)
    {
        ArgumentNullException.ThrowIfNull(user);
        if (string.IsNullOrWhiteSpace(newPasswordPlaintext))
            throw new ArgumentException("Plaintext is required for reuse detection.", nameof(newPasswordPlaintext));

        // Current hash first — usually the most common reuse case.
        if (_hasher.Verify(newPasswordPlaintext, user.PasswordHash))
            return true;

        // Then the prior five (newest-first projection already filters the
        // retained set; we trust the domain to cap retention at five).
        foreach (var entry in user.PasswordHistory)
        {
            if (_hasher.Verify(newPasswordPlaintext, entry.Hash))
                return true;
        }

        return false;
    }
}