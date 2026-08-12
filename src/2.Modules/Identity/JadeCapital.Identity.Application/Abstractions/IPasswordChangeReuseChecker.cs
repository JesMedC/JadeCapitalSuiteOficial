using JadeCapital.Identity.Domain.Users;

namespace JadeCapital.Identity.Application.Abstractions;

/// <summary>
/// Application-boundary service that detects plaintext-password reuse against
/// a user's CURRENT credential AND any of their PRIOR five retained hashes.
///
/// The domain stores salted PBKDF2 hashes and therefore cannot reliably
/// detect reuse by encoded-hash string comparison — the same plaintext
/// produces a different encoded hash on every call. This interface is the
/// ONLY legitimate place where plaintext meets stored hashes for reuse
/// detection; the domain stays unaware of plaintext credentials.
/// </summary>
public interface IPasswordChangeReuseChecker
{
    /// <summary>
    /// Returns true iff the candidate plaintext matches the user's current
    /// hash OR any of the prior five retained hashes (per
    /// <see cref="User.PasswordHistory"/>). Hash comparison uses
    /// <see cref="IPasswordHasher.Verify"/> and is therefore salted and
    /// constant-time.
    /// </summary>
    bool IsReused(User user, string newPasswordPlaintext);
}