namespace JadeCapital.Identity.UnitTests.Authentication;

/// <summary>
/// Domain tests for password-history retention and User.SessionVersion — slice 0a.
///
/// Covers:
/// - History ordering: (changed_at DESC, id DESC).
/// - 5-newest retention with eviction of older entries.
/// - Rejection of reuse against current credential AND prior 5.
/// - SessionVersion increments on every successful password change.
/// </summary>
public class PasswordHistoryTests
{
    private static User CreateActiveUser(string initialHash = "initial-hash")
        => User.Register(Guid.NewGuid(), "user" + "@" + "test.com", "TestUser", initialHash, UserRole.Trader).Value;

    // ============================================
    // User.SessionVersion
    // ============================================

    [Fact]
    public void NewUser_SessionVersion_DefaultsToZero()
    {
        var u = CreateActiveUser();

        u.SessionVersion.Should().Be(0);
    }

    [Fact]
    public void ChangePasswordPreservingHistory_IncrementsSessionVersion()
    {
        var u = CreateActiveUser();

        var r = u.ChangePasswordPreservingHistory("hash-1");

        r.IsSuccess.Should().BeTrue();
        u.SessionVersion.Should().Be(1);
    }

    // ============================================
    // History: insertion order (changed_at DESC, id DESC)
    // ============================================

    [Fact]
    public void FirstChange_PrependsPreviousCurrentHash_IntoHistory()
    {
        var u = CreateActiveUser("initial");

        var r = u.ChangePasswordPreservingHistory("hash-1");

        r.IsSuccess.Should().BeTrue();
        u.PasswordHash.Should().Be("hash-1");
        u.PasswordHistory.Should().HaveCount(1);
        u.PasswordHistory[0].Hash.Should().Be("initial");
    }

    [Fact]
    public void MultipleChanges_OrderHistoryNewestFirst()
    {
        var u = CreateActiveUser("initial");

        u.ChangePasswordPreservingHistory("hash-1");
        u.ChangePasswordPreservingHistory("hash-2");
        u.ChangePasswordPreservingHistory("hash-3");

        u.PasswordHistory.Should().HaveCount(3);
        u.PasswordHistory[0].Hash.Should().Be("hash-2"); // most recently displaced
        u.PasswordHistory[1].Hash.Should().Be("hash-1");
        u.PasswordHistory[2].Hash.Should().Be("initial");
    }

    [Fact]
    public void PasswordHistoryEntry_OrderNewestFirst_UsesChangedAtDescThenIdDesc()
    {
        // Two entries with the same ChangedAt — tie-breaker must be id DESC.
        var sharedTime = DateTimeOffset.UtcNow;
        var olderId = Guid.Parse("00000000-0000-0000-0000-000000000001");
        var newerId = Guid.Parse("00000000-0000-0000-0000-000000000002");

        var older = PasswordHistoryEntry.Create(olderId, Guid.NewGuid(), "older", sharedTime);
        var newer = PasswordHistoryEntry.Create(newerId, Guid.NewGuid(), "newer", sharedTime);

        var ordered = PasswordHistoryEntry.OrderNewestFirst(new[] { older, newer });

        ordered[0].Id.Should().Be(newerId); // tie-breaker: id DESC
        ordered[1].Id.Should().Be(olderId);
    }

    // ============================================
    // 5-newest retention
    // ============================================

    [Fact]
    public void SeventhChange_RetainsOnlyFiveNewestInHistory()
    {
        var u = CreateActiveUser("h0");

        u.ChangePasswordPreservingHistory("h1");
        u.ChangePasswordPreservingHistory("h2");
        u.ChangePasswordPreservingHistory("h3");
        u.ChangePasswordPreservingHistory("h4");
        u.ChangePasswordPreservingHistory("h5");
        u.ChangePasswordPreservingHistory("h6");

        // 6 successful changes produced 6 history entries (one displaced per change).
        // Retention keeps newest 5 — "h1" should be evicted, "h0" already evicted.
        u.PasswordHistory.Should().HaveCount(5);
        u.PasswordHistory.Select(e => e.Hash).Should().Equal("h5", "h4", "h3", "h2", "h1");
        u.PasswordHistory.Should().NotContain(e => e.Hash == "h0");
    }

    // ============================================
    // Plaintext reuse rejection lives in Application
    // (PasswordChangeReuseChecker) — see PasswordChangeReuseCheckerTests.
    // The domain only stores hashes; salted PBKDF2 means the same plaintext
    // produces different encoded hashes, so the domain cannot reliably detect
    // reuse by string comparison. Clean Architecture: plaintext-vs-stored-hash
    // verification belongs to IPasswordHasher.Verify in the Application layer.
    // ============================================
}

/// <summary>
/// Verifies that history normalization happens BEFORE prepend/eviction so
/// hydration order cannot evict the wrong hash. Uses reflection on the
/// private backing field as a "controlled internal test mechanism" — the
/// domain exposes no public hydration API.
/// </summary>
public class PasswordHistoryHydrationOrderTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;

    private static User CreateActiveUser(string initialHash = "initial-hash")
        => User.Register(Guid.NewGuid(), "user" + "@" + "test.com", "TestUser", initialHash, UserRole.Trader).Value;

    private static List<PasswordHistoryEntry> GetBackingHistory(User u)
    {
        var field = typeof(User).GetField("_passwordHistory",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
        return (List<PasswordHistoryEntry>)field.GetValue(u)!;
    }

    [Fact]
    public void ChangePasswordPreservingHistory_NormalizesUnorderedHydratedHistory_EvictsOldestNotArbitrary()
    {
        var u = CreateActiveUser("initial");

        // Inject 4 entries in ARBITRARY order to simulate EF hydration
        // without the (changed_at DESC, id DESC) index having been used.
        var backing = GetBackingHistory(u);
        backing.Clear();
        backing.Add(PasswordHistoryEntry.Create(Guid.NewGuid(), u.Id, "h-oldest", Now.AddDays(-3)));
        backing.Add(PasswordHistoryEntry.Create(Guid.NewGuid(), u.Id, "h-newer", Now.AddDays(-1)));
        backing.Add(PasswordHistoryEntry.Create(Guid.NewGuid(), u.Id, "h-newest", Now));
        backing.Add(PasswordHistoryEntry.Create(Guid.NewGuid(), u.Id, "h-middle", Now.AddDays(-2)));

        // Two changes push us past MaxPasswordHistoryEntries=5. Without
        // normalization, Insert(0) + RemoveAt(end) would evict whichever
        // entry happens to land at the tail of the unordered backing list.
        u.ChangePasswordPreservingHistory("h-change-1").IsSuccess.Should().BeTrue();
        u.ChangePasswordPreservingHistory("h-change-2").IsSuccess.Should().BeTrue();

        // After normalization the five retained hashes are (newest first):
        //   h-change-1 (most recently displaced)
        //   initial    (previously current, displaced by change-2)
        //   h-newest   (injected, newest by ChangedAt)
        //   h-newer    (injected, 2nd newest)
        //   h-middle   (injected, 3rd newest) — h-oldest MUST be evicted
        u.PasswordHistory.Select(e => e.Hash).Should().Equal(
            "h-change-1", "initial", "h-newest", "h-newer", "h-middle");
        u.PasswordHistory.Should().NotContain(e => e.Hash == "h-oldest");
    }
}
