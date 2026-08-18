using FluentAssertions;
using JadeCapital.Shared.Kernel.SoftDelete;
using NSubstitute;

namespace JadeCapital.Shared.Kernel.UnitTests.SoftDelete;

/// <summary>
/// Contract tests for <see cref="ISoftDelete"/> (Wave 6, slice 6d.1).
///
/// <para>
/// The interface pins the soft-delete shape that every user-owned aggregate
/// in Wave 6+ MUST implement. Three RED scenarios pinned here:
/// </para>
/// <list type="number">
///   <item><b>Interface shape</b>: exactly three members (<c>IsDeleted</c>,
///         <c>DeletedAtUtc</c>, <c>DeletedByUserId</c>) — no accidental
///         expansion (the ISoftDelete contract is cross-module; widening it
///         is a breaking change).</item>
///   <item><b>IsDeleted defaults to false</b> in a placeholder impl — fresh
///         entities must NOT report themselves as deleted.</item>
///   <item><b>DeletedAtUtc is nullable</b> — only set after <c>MarkDeleted</c>;
///         reflects that the underlying column is nullable (NOT soft-deleted
///         ⇒ NULL).</item>
/// </list>
///
/// <para>
/// Adding a fourth member (e.g. a restore timestamp) is a breaking change.
/// The shape is frozen here so reviewers can grep for any drift.
/// </para>
/// </summary>
public class ISoftDeleteContractTests
{
    [Fact]
    public void ISoftDelete_DeclaresExactlyThreeMembers()
    {
        var iface = typeof(ISoftDelete);
        var members = iface.GetProperties()
            .Select(p => p.Name)
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToArray();

        members.Should().BeEquivalentTo(new[]
        {
            "DeletedAtUtc",
            "DeletedByUserId",
            "IsDeleted",
        });
    }

    [Fact]
    public void IsDeleted_DefaultsToFalse_InPlaceholder()
    {
        // The placeholder is a sentinel test double — the real entities
        // (ImportJob etc.) ship later in this slice. Pinning the default
        // behavior now catches accidental initializers (e.g. a future
        // contributor who writes `IsDeleted { get; set; } = true;`).
        ISoftDelete placeholder = new PlaceholderSoftDelete();

        placeholder.IsDeleted.Should().BeFalse();
    }

    [Fact]
    public void DeletedAtUtc_AndDeletedByUserId_AreNullable()
    {
        // DateTimeOffset? reflects Nullable<DateTimeOffset>.
        // Guid? reflects Nullable<Guid>.
        // The column-level nullability mirrors this — NOT soft-deleted
        // ⇒ NULL, soft-deleted ⇒ timestamp + user id.
        var iface = typeof(ISoftDelete);

        var deletedAt = iface.GetProperty("DeletedAtUtc")!;
        Nullable.GetUnderlyingType(deletedAt.PropertyType)
            .Should().Be<DateTimeOffset>(
                $"DeletedAtUtc must be DateTimeOffset? (nullable). Actual: {deletedAt.PropertyType.FullName}");

        var deletedBy = iface.GetProperty("DeletedByUserId")!;
        Nullable.GetUnderlyingType(deletedBy.PropertyType)
            .Should().Be<Guid>(
                $"DeletedByUserId must be Guid? (nullable). Actual: {deletedBy.PropertyType.FullName}");
    }

    /// <summary>
    /// Placeholder impl used to pin the default-behavior contract. The real
    /// entities (ImportJob in this slice; Tenant + others in Wave 7) ship
    /// their own implementations.
    /// </summary>
    private sealed class PlaceholderSoftDelete : ISoftDelete
    {
        public bool IsDeleted => false;
        public DateTimeOffset? DeletedAtUtc => null;
        public Guid? DeletedByUserId => null;
    }
}
