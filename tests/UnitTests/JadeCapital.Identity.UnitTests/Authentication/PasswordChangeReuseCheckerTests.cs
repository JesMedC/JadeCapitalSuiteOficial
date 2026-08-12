using System.Security.Cryptography;
using System.Text;
using JadeCapital.Identity.Application.Abstractions;
using JadeCapital.Identity.Application.Authentication;

namespace JadeCapital.Identity.UnitTests.Authentication;

/// <summary>
/// Application-layer tests for <see cref="PasswordChangeReuseChecker"/>.
///
/// Salted PBKDF2 produces a different encoded hash for the same plaintext on
/// every call. The domain therefore cannot detect reuse by hash string
/// comparison — that responsibility belongs to the Application boundary, which
/// holds the IPasswordHasher that knows the salt format and can verify
/// plaintext against the stored hash.
///
/// These tests use a deliberately salted test hasher so the same plaintext
/// yields a different encoded hash on every Hash() call.
/// </summary>
public class PasswordChangeReuseCheckerTests
{
    private static readonly TestSaltedHasher Hasher = new();

    private static User RegisterWithHash(string plaintext)
        => User.Register(Guid.NewGuid(), "u" + "@" + "t.com", "Test", Hasher.Hash(plaintext), UserRole.Trader).Value;

    [Fact]
    public void IsReused_SamePlaintextAsCurrent_DetectedDespiteDifferentSaltedHash()
    {
        var u = RegisterWithHash("hunter2!");
        var checker = new PasswordChangeReuseChecker(Hasher);

        // Sanity: hashing the same plaintext yields a DIFFERENT encoded hash.
        var freshHash = Hasher.Hash("hunter2!");
        freshHash.Should().NotBe(u.PasswordHash);

        checker.IsReused(u, "hunter2!").Should().BeTrue();
    }

    [Fact]
    public void IsReused_SamePlaintextAsAnyOfPreviousFive_DetectedDespiteDifferentSaltedHashes()
    {
        var u = RegisterWithHash("plain-0");
        u.ChangePasswordPreservingHistory(Hasher.Hash("plain-1"));
        u.ChangePasswordPreservingHistory(Hasher.Hash("plain-2"));
        u.ChangePasswordPreservingHistory(Hasher.Hash("plain-3"));
        u.ChangePasswordPreservingHistory(Hasher.Hash("plain-4"));
        u.ChangePasswordPreservingHistory(Hasher.Hash("plain-5"));
        u.ChangePasswordPreservingHistory(Hasher.Hash("plain-current"));

        var checker = new PasswordChangeReuseChecker(Hasher);

        // "plain-5" .. "plain-1" are the prior 5 in history (newest first).
        // Each prior hash was salted independently — same plaintext produces
        // a different encoded hash, but Verify still matches.
        foreach (var prior in new[] { "plain-5", "plain-4", "plain-3", "plain-2", "plain-1" })
        {
            checker.IsReused(u, prior).Should().BeTrue($"reusing {prior} must be detected");
        }

        // The current password is also rejected.
        checker.IsReused(u, "plain-current").Should().BeTrue();

        // A password that was never used is accepted.
        checker.IsReused(u, "fresh-plaintext-123").Should().BeFalse();
    }

    [Fact]
    public void IsReused_PlaintextNeverUsed_ReturnsFalse()
    {
        var u = RegisterWithHash("first-plaintext");
        u.ChangePasswordPreservingHistory(Hasher.Hash("second-plaintext"));
        var checker = new PasswordChangeReuseChecker(Hasher);

        checker.IsReused(u, "third-plaintext-9876").Should().BeFalse();
    }

    [Fact]
    public void IsReused_DistinguishesCaseAndWhitespace_AsDifferentPlaintexts()
    {
        var u = RegisterWithHash("Password123!");
        var checker = new PasswordChangeReuseChecker(Hasher);

        checker.IsReused(u, "Password123!").Should().BeTrue();
        checker.IsReused(u, "password123!").Should().BeFalse();
        checker.IsReused(u, " Password123! ").Should().BeFalse();
    }

    [Fact]
    public void IsReused_NullOrEmptyPlaintext_FailsClosed()
    {
        var u = RegisterWithHash("valid-plaintext");
        var checker = new PasswordChangeReuseChecker(Hasher);

        // The Application layer must reject obviously-invalid plaintext BEFORE
        // even reaching the hasher. Empty/whitespace could otherwise confuse
        // any permissive Verify implementation.
        Action nullCheck = () => checker.IsReused(u, null!);
        Action emptyCheck = () => checker.IsReused(u, "");
        Action whitespaceCheck = () => checker.IsReused(u, "   ");

        nullCheck.Should().Throw<ArgumentException>();
        emptyCheck.Should().Throw<ArgumentException>();
        whitespaceCheck.Should().Throw<ArgumentException>();
    }

    /// <summary>
    /// Deliberately salted: every Hash() call yields a fresh random 16-byte
    /// salt, encodes it as base64, and writes "{saltB64}.{sha256(salt || pwd)B64}".
    /// Verify is constant-time via CryptographicOperations.FixedTimeEquals.
    /// </summary>
    private sealed class TestSaltedHasher : IPasswordHasher
    {
        public string Hash(string password)
        {
            if (string.IsNullOrEmpty(password))
                throw new ArgumentException("Password cannot be empty.", nameof(password));

            var salt = new byte[16];
            RandomNumberGenerator.Fill(salt);
            var hash = SHA256.HashData(salt.Concat(Encoding.UTF8.GetBytes(password)).ToArray());
            return $"{Convert.ToBase64String(salt)}.{Convert.ToBase64String(hash)}";
        }

        public bool Verify(string password, string stored)
        {
            if (string.IsNullOrEmpty(password) || string.IsNullOrEmpty(stored)) return false;
            var parts = stored.Split('.', 2);
            if (parts.Length != 2) return false;

            try
            {
                var salt = Convert.FromBase64String(parts[0]);
                var expected = Convert.FromBase64String(parts[1]);
                var actual = SHA256.HashData(salt.Concat(Encoding.UTF8.GetBytes(password)).ToArray());
                return CryptographicOperations.FixedTimeEquals(actual, expected);
            }
            catch
            {
                return false;
            }
        }
    }
}