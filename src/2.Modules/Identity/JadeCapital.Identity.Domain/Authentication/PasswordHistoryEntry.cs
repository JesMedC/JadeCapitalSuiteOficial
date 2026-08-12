using JadeCapital.Identity.Domain.Common;
using JadeCapital.Shared.Kernel.Primitives;
using JadeCapital.Shared.Kernel.Results;

namespace JadeCapital.Identity.Domain.Authentication;

/// <summary>
/// Snapshot of a previously-current password hash, kept so the user cannot
/// rotate back to it. Ordered by (changed_at DESC, id DESC) — newest
/// displaced credential first; the Guid Id breaks timestamp ties deterministically.
/// </summary>
public sealed class PasswordHistoryEntry : Entity<Guid>
{
    public Guid UserId { get; private set; }
    public string Hash { get; private set; } = default!;
    public DateTimeOffset ChangedAt { get; private set; }

    // EF Core.
    private PasswordHistoryEntry() { }

    private PasswordHistoryEntry(Guid id, Guid userId, string hash, DateTimeOffset changedAt)
        : base(id)
    {
        UserId = userId;
        Hash = hash;
        ChangedAt = changedAt;
    }

    /// <summary>
    /// Factory for the password-history write path (User.ChangePasswordPreservingHistory).
    /// Trusts the caller to provide a non-empty hash and a positive Guid.
    /// </summary>
    public static PasswordHistoryEntry Create(Guid id, Guid userId, string hash, DateTimeOffset changedAt)
    {
        if (id == Guid.Empty)
            throw new ArgumentException("PasswordHistoryEntry id required.", nameof(id));
        if (userId == Guid.Empty)
            throw new ArgumentException("PasswordHistoryEntry userId required.", nameof(userId));
        if (string.IsNullOrWhiteSpace(hash))
            throw new ArgumentException("PasswordHistoryEntry hash required.", nameof(hash));
        return new PasswordHistoryEntry(id, userId, hash, changedAt);
    }

    /// <summary>
    /// Result-returning factory used by EF hydration or other code paths
    /// that want explicit failure on invalid input.
    /// </summary>
    public static Result<PasswordHistoryEntry> Create(
        Guid id,
        Guid userId,
        string hash,
        DateTimeOffset changedAt,
        bool strict)
    {
        if (id == Guid.Empty)
            return Result.Failure<PasswordHistoryEntry>(IdentityDomainErrors.PasswordHistory.IdRequired);
        if (userId == Guid.Empty)
            return Result.Failure<PasswordHistoryEntry>(IdentityDomainErrors.User.IdRequired);
        if (string.IsNullOrWhiteSpace(hash))
            return Result.Failure<PasswordHistoryEntry>(IdentityDomainErrors.Credential.HashRequired);
        return Result.Success(new PasswordHistoryEntry(id, userId, hash, changedAt));
    }

    /// <summary>
    /// Returns the entries ordered (changed_at DESC, id DESC) — newest first,
    /// stable on the Guid tie-breaker. The DB index matches this ordering.
    /// </summary>
    public static IReadOnlyList<PasswordHistoryEntry> OrderNewestFirst(
        IEnumerable<PasswordHistoryEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        return entries
            .OrderByDescending(e => e.ChangedAt)
            .ThenByDescending(e => e.Id)
            .ToList();
    }
}
