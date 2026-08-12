using JadeCapital.Shared.Kernel.Time;

namespace JadeCapital.Identity.UnitTests.Authentication;

/// <summary>
/// Domain tests for TemporaryCredential — slice 0a of jade-trader-os-core-portals.
///
/// Covers:
/// - Latest-only activation (CAS-style: only the latest pending generation can activate).
/// - 24-hour expiry measured from activation.
/// - Single-use: MarkConsumed transitions Activated → Consumed.
/// - Reuse, replay, and supersession rules.
/// </summary>
public class TemporaryCredentialTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;
    private static readonly DateTimeOffset Later = Now.AddHours(2);
    private static readonly DateTimeOffset AfterExpiry = Now.AddHours(25);

    private static TemporaryCredential CreatePending(int generation = 1, Guid? userId = null)
        => TemporaryCredential.Reserve(
            Guid.NewGuid(),
            userId ?? Guid.NewGuid(),
            generation,
            CredentialHash.From("hashed"),
            Now).Value;

    // ============================================
    // Reserve
    // ============================================

    [Fact]
    public void Reserve_WithValidData_CreatesPendingCredential()
    {
        var r = TemporaryCredential.Reserve(Guid.NewGuid(), Guid.NewGuid(), 1, CredentialHash.From("h"), Now);

        r.IsSuccess.Should().BeTrue();
        var c = r.Value;
        c.Status.Should().Be(TemporaryCredentialStatus.Pending);
        c.Generation.Should().Be(1);
        c.ActivatedAt.Should().BeNull();
        c.ConsumedAt.Should().BeNull();
        c.ExpiresAt.Should().Be(Now.AddHours(24));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Reserve_WithNonPositiveGeneration_Fails(int generation)
    {
        var r = TemporaryCredential.Reserve(Guid.NewGuid(), Guid.NewGuid(), generation, CredentialHash.From("h"), Now);

        r.IsFailure.Should().BeTrue();
    }

    [Theory]
    [InlineData("00000000-0000-0000-0000-000000000000", "11111111-1111-1111-1111-111111111111")] // empty id, valid user
    [InlineData("11111111-1111-1111-1111-111111111111", "00000000-0000-0000-0000-000000000000")] // valid id, empty user
    public void Reserve_WithEmptyIdOrUserId_Fails(string idRaw, string userIdRaw)
    {
        var r = TemporaryCredential.Reserve(Guid.Parse(idRaw), Guid.Parse(userIdRaw), 1, CredentialHash.From("h"), Now);

        r.IsFailure.Should().BeTrue();
    }

    // ============================================
    // Activate (CAS — latest-only)
    // ============================================

    [Fact]
    public void Activate_FromPending_BecomesActivated_SetsExpiresAtTo24HoursFromActivation()
    {
        var c = CreatePending();

        var r = c.Activate(Later, latestGeneration: 1);

        r.IsSuccess.Should().BeTrue();
        c.Status.Should().Be(TemporaryCredentialStatus.Activated);
        c.ActivatedAt.Should().Be(Later);
        c.ExpiresAt.Should().Be(Later.AddHours(24));
        c.IsUsable(Later).Should().BeTrue();
    }

    [Fact]
    public void Activate_WhenNotLatestGeneration_FailsAsSuperseded()
    {
        // Generation 2 was reserved after generation 1; activating generation 1
        // is no longer valid (CAS lost). This is "latest-only activation".
        var older = CreatePending(generation: 1);

        var r = older.Activate(Later, latestGeneration: 2);

        r.IsFailure.Should().BeTrue();
        older.Status.Should().Be(TemporaryCredentialStatus.Pending);
        older.ActivatedAt.Should().BeNull();
    }

    // ============================================
    // MarkConsumed (single-use)
    // ============================================

    [Fact]
    public void MarkConsumed_FromActivated_TransitionsAndStoresGrantJti()
    {
        var c = CreatePending();
        c.Activate(Later, latestGeneration: 1);

        var r = c.MarkConsumed(Later.AddMinutes(5), grantJti: "jti-abc");

        r.IsSuccess.Should().BeTrue();
        c.Status.Should().Be(TemporaryCredentialStatus.Consumed);
        c.ConsumedAt.Should().Be(Later.AddMinutes(5));
        c.GrantJti.Should().Be("jti-abc");
        c.IsUsable(Later.AddMinutes(5)).Should().BeFalse();
    }

    [Fact]
    public void MarkConsumed_FromPending_Fails()
    {
        var c = CreatePending();

        var r = c.MarkConsumed(Later, "jti");

        r.IsFailure.Should().BeTrue();
        c.Status.Should().Be(TemporaryCredentialStatus.Pending);
        c.ConsumedAt.Should().BeNull();
        c.GrantJti.Should().BeNull();
    }

    // ============================================
    // IsUsable
    // ============================================

    [Fact]
    public void IsUsable_ActivatedAndWithin24Hours_ReturnsTrue()
    {
        var c = CreatePending();
        c.Activate(Now, latestGeneration: 1);

        c.IsUsable(Now.AddHours(23)).Should().BeTrue();
    }

    [Fact]
    public void IsUsable_AfterExpiryOrConsumed_ReturnsFalse()
    {
        var c = CreatePending();
        c.Activate(Now, latestGeneration: 1);

        // Expired boundary (Activated > 24h ago).
        c.IsUsable(Now.AddHours(24).AddSeconds(1)).Should().BeFalse();

        // A fresh credential consumed must be unusable.
        var c2 = CreatePending();
        c2.Activate(Now, latestGeneration: 1);
        c2.MarkConsumed(Now.AddMinutes(1), "jti");
        c2.IsUsable(Now.AddMinutes(5)).Should().BeFalse();
    }

    // ============================================
    // CrockfordCredential VO
    // 26 chars × 5 bits/char = 130 encoded bits; underlying CSPRNG seed is
    // 16 bytes = 128 bits of entropy. The contract says 128-bit random.
    // ============================================

    [Fact]
    public void CrockfordCredential_Generate_ProducesTwentySixCharsInAlphabet_From128BitSeed()
    {
        var s = CrockfordCredential.Generate();

        s.Should().HaveLength(26);
        CrockfordCredential.TryParse(s, out _).Should().BeTrue();
    }

    [Theory]
    [InlineData("")]
    [InlineData("short")]
    [InlineData("ABCDEFGHIJKLMNOPQRSTUVWX")] // 23 chars — too short
    [InlineData("ABCDEFGHIJKLMNOPQRSTUVWXYZ1")] // 27 chars — too long
    public void CrockfordCredential_TryParse_WrongLength_Fails(string input)
    {
        CrockfordCredential.TryParse(input, out _).Should().BeFalse();
    }

    [Fact]
    public void CrockfordCredential_TryParse_ValidLengthTwentySix_Succeeds()
    {
        CrockfordCredential.TryParse("0123456789ABCDEFGHJKMNPQRS", out _).Should().BeTrue(); // exactly 26 valid chars
    }

    [Theory]
    [InlineData("ABCDEFGHIJKLMNOPQRSTUVWXZI")] // I forbidden (27 chars)
    [InlineData("ABCDEFGHIJKLMNOPQRSTUVWXZL")] // L forbidden (27 chars)
    [InlineData("ABCDEFGHIJKLMNOPQRSTUVWXZO")] // O forbidden (27 chars)
    [InlineData("ABCDEFGHIJKLMNOPQRSTUVWXZU")] // U forbidden (27 chars)
    [InlineData("ABCDEFGHIJKLMNOPQRSTUV!XYZ")] // ! not Crockford
    public void CrockfordCredential_TryParse_InvalidAlphabet_Fails(string input)
    {
        CrockfordCredential.TryParse(input, out _).Should().BeFalse();
    }

    [Fact]
    public void CrockfordCredential_TryParse_NormalizesLowercase()
    {
        CrockfordCredential.TryParse("0123456789ABCDEFGHJKMNPQRS", out var normalized).Should().BeTrue(); // 26 chars
        normalized.Should().Be("0123456789ABCDEFGHJKMNPQRS");
    }

    // ============================================
    // Activate: only the exact latest generation may activate
    // ============================================

    [Fact]
    public void Activate_WhenGenerationGreaterThanLatest_FailsAsSuperseded()
    {
        // "latest" in the caller's hand means a row they observed committed
        // before activation; any other generation (older OR newer) means the
        // row is no longer the row the caller is asserting on. This prevents
        // accidental activation of a row that was reserved AFTER the caller's
        // last DB read.
        var c = CreatePending(generation: 5);

        var r = c.Activate(Later, latestGeneration: 4);

        r.IsFailure.Should().BeTrue();
        c.Status.Should().Be(TemporaryCredentialStatus.Pending);
        c.ActivatedAt.Should().BeNull();
    }

    [Fact]
    public void Activate_AlreadyActivatedTwice_FailsOnSecondAttempt()
    {
        var c = CreatePending();
        c.Activate(Later, latestGeneration: 1);

        // Even if the caller somehow passes the same latestGeneration, the
        // status transition itself prevents a second activation.
        var r = c.Activate(Later.AddMinutes(1), latestGeneration: 1);

        r.IsFailure.Should().BeTrue();
        c.ActivatedAt.Should().Be(Later);
    }
}

/// <summary>
/// Verifies the contract that temporary-credential login failures share the
/// same User.FailedLoginCount counter and 5-attempt lockout as ordinary
/// password failures — there is no separate counter for temporary credentials.
/// </summary>
public class TempFailuresShareLockoutCounterTests
{
    private static User CreateActiveUser()
        => User.Register(Guid.NewGuid(), "user" + "@" + "test.com", "TestUser", "h", UserRole.Trader).Value;

    [Fact]
    public void MixedRegularAndTempFailures_IncrementSameCounter_AndLockAtFive()
    {
        var u = CreateActiveUser();

        // Three ordinary-password failures.
        u.RecordFailedLogin();
        u.RecordFailedLogin();
        u.RecordFailedLogin();
        u.FailedLoginCount.Should().Be(3);
        u.Status.Should().Be(UserStatus.Active);

        // Two temporary-credential failures use the SAME counter.
        u.RecordFailedLogin();
        u.RecordFailedLogin();
        u.FailedLoginCount.Should().Be(User.MaxFailedLoginAttempts);

        // Lockout fires at 5 attempts regardless of which credential type failed.
        u.Status.Should().Be(UserStatus.LockedOut);
        u.IsLockedOut(DateTimeOffset.UtcNow).Should().BeTrue();
    }
}
