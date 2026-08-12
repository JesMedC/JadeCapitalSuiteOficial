using JadeCapital.Shared.Kernel.Results;

namespace JadeCapital.Identity.Domain.Authentication;

/// <summary>
/// Strongly-typed wrapper around a stored credential hash (PBKDF2 output).
/// Domain never accepts an empty hash — handlers MUST hash plaintext
/// before constructing this value object. Plaintext credentials never
/// enter or leave the domain layer.
/// </summary>
public readonly record struct CredentialHash
{
    public string Value { get; }

    private CredentialHash(string value) => Value = value;

    public static Result<CredentialHash> Create(string hash)
    {
        if (string.IsNullOrWhiteSpace(hash))
            return Result.Failure<CredentialHash>(
                Error.Validation("credential.hash_required", "Credential hash is required."));
        return Result.Success(new CredentialHash(hash));
    }

    /// <summary>
    /// Convenience factory for callers that already validated non-emptiness
    /// (e.g. EF hydration, seed scripts, or trusted internal callers).
    /// Throws if the hash is empty — domain invariants MUST be upheld.
    /// </summary>
    public static CredentialHash From(string hash)
    {
        if (string.IsNullOrWhiteSpace(hash))
            throw new ArgumentException("Credential hash cannot be empty.", nameof(hash));
        return new CredentialHash(hash);
    }

    public override string ToString() => Value;

    public static implicit operator string(CredentialHash hash) => hash.Value;
}
