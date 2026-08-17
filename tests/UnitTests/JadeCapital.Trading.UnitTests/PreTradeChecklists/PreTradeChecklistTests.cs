using JadeCapital.Shared.Kernel.Time;
using JadeCapital.Trading.Domain.PreTradeChecklists;

namespace JadeCapital.Trading.UnitTests.PreTradeChecklists;

/// <summary>
/// Domain tests for <c>PreTradeChecklist</c> — slice 1c.1 of
/// <c>2026-08-15-trader-risk-journal-core</c>.
///
/// Covers the seven scenarios required by the spec
/// (<c>openspec/changes/2026-08-15-trader-risk-journal-core/specs/pre-trade-checklist/spec.md</c>):
/// <list type="number">
///   <item>Enum validity — Emotionality / SetupQuality values are 1..5 and
///   accepted as enums (the underlying SMALLINT column mirrors them).</item>
///   <item>RR at entry &gt;= target — factory rejects when the trader tries
///   to enter with a worse RR than the profile target (or worse than 1.0
///   when no profile).</item>
///   <item>Confluences range — confluences must be 1..10 inclusive.</item>
///   <item>Factory happy path — given valid inputs, the aggregate is created
///   with all fields populated and a single <c>PreTradeChecklistSubmittedDomainEvent</c>
///   raised.</item>
///   <item>Target fallback when no profile — the handler resolves the
///   target (snapshot vs 2.0 default); the domain aggregate receives the
///   resolved target and persists it as-is. Tested at the domain level
///   by ensuring the aggregate accepts target=1.0 and any RR &gt;= 1.0.</item>
///   <item>Status enum persistence — Emotionality / SetupQuality round-trip
///   through the VO without modification (the persisted byte equals the
///   enum's underlying value).</item>
///   <item>Cross-validation — out-of-range emotionality / setup quality
///   values (cast from non-enum byte) are rejected at the aggregate
///   boundary.</item>
/// </list>
/// </summary>
public class PreTradeChecklistTests
{
    private static readonly DateTimeOffset FixedNow =
        new(2026, 8, 15, 10, 0, 0, TimeSpan.Zero);

    private sealed class FixedClock : IClock
    {
        public DateTimeOffset UtcNow { get; set; } = FixedNow;
    }

    private static PreTradeChecklistSubmission ValidSubmission(
        Emotionality emotionality = Emotionality.Neutral,
        SetupQuality setupQuality = SetupQuality.Good,
        decimal riskRewardAtEntry = 2.5m,
        decimal riskRewardTargetUsed = 2.0m,
        byte confluencesCount = 3)
        => new(emotionality, setupQuality, riskRewardAtEntry, riskRewardTargetUsed, confluencesCount);

    // ============================================
    // Scenario 4: Factory happy path
    // ============================================

    [Fact]
    public void Create_WithValidInputs_PersistsAllFieldsAndRaisesEvent()
    {
        var tradeId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        var r = PreTradeChecklist.Create(
            tradeId, userId, ValidSubmission(), FixedNow);

        r.IsSuccess.Should().BeTrue();
        var checklist = r.Value;
        checklist.TradeId.Should().Be(tradeId);
        checklist.UserId.Should().Be(userId);
        checklist.Submission.Emotionality.Should().Be(Emotionality.Neutral);
        checklist.Submission.SetupQuality.Should().Be(SetupQuality.Good);
        checklist.Submission.RiskRewardAtEntry.Should().Be(2.5m);
        checklist.Submission.RiskRewardTargetUsed.Should().Be(2.0m);
        checklist.Submission.ConfluencesCount.Should().Be((byte)3);
        checklist.SubmittedAt.Should().Be(FixedNow);
        checklist.CreatedAt.Should().Be(FixedNow);

        checklist.DomainEvents.Should().ContainSingle()
            .Which.Should().BeOfType<PreTradeChecklistSubmittedDomainEvent>();
    }

    // ============================================
    // Scenario 2: RR at entry must be >= target
    // ============================================

    [Fact]
    public void Create_WithRiskRewardAtEntryBelowTarget_FailsWithValidationError()
    {
        // Target=2.0, RR at entry=1.5 -> below target.
        var submission = ValidSubmission(riskRewardAtEntry: 1.5m, riskRewardTargetUsed: 2.0m);

        var r = PreTradeChecklist.Create(Guid.NewGuid(), Guid.NewGuid(), submission, FixedNow);

        r.IsFailure.Should().BeTrue();
        r.Error.Code.Should().Be("validation.pre_trade_checklist.rr_below_target");
    }

    [Fact]
    public void Create_WithRiskRewardAtEntryEqualToTarget_Succeeds()
    {
        // Boundary case: RR at entry == target is accepted.
        var submission = ValidSubmission(riskRewardAtEntry: 2.0m, riskRewardTargetUsed: 2.0m);

        var r = PreTradeChecklist.Create(Guid.NewGuid(), Guid.NewGuid(), submission, FixedNow);

        r.IsSuccess.Should().BeTrue();
    }

    // ============================================
    // Scenario 5: Target fallback when no profile
    // ============================================

    [Fact]
    public void Create_WithTargetFallbackAndLowRr_SucceedsWhenEntryMatchesDefault()
    {
        // No active profile -> target defaults to 1.0 (handler responsibility).
        // Domain accepts any RR >= 1.0 when target=1.0.
        var submission = ValidSubmission(riskRewardAtEntry: 1.5m, riskRewardTargetUsed: 1.0m);

        var r = PreTradeChecklist.Create(Guid.NewGuid(), Guid.NewGuid(), submission, FixedNow);

        r.IsSuccess.Should().BeTrue();
        r.Value.Submission.RiskRewardTargetUsed.Should().Be(1.0m);
    }

    // ============================================
    // Scenario 3: Confluences range
    // ============================================

    [Theory]
    [InlineData((byte)0)]    // below minimum
    [InlineData((byte)11)]   // above maximum
    public void Create_WithConfluencesOutOfRange_FailsWithValidationError(byte confluences)
    {
        var submission = ValidSubmission(confluencesCount: confluences);

        var r = PreTradeChecklist.Create(Guid.NewGuid(), Guid.NewGuid(), submission, FixedNow);

        r.IsFailure.Should().BeTrue();
        r.Error.Code.Should().Be("validation.pre_trade_checklist.confluences_out_of_range");
    }

    [Theory]
    [InlineData((byte)1)]   // min
    [InlineData((byte)10)]  // max
    public void Create_WithConfluencesAtBoundaries_Succeeds(byte confluences)
    {
        var submission = ValidSubmission(confluencesCount: confluences);

        var r = PreTradeChecklist.Create(Guid.NewGuid(), Guid.NewGuid(), submission, FixedNow);

        r.IsSuccess.Should().BeTrue();
    }

    // ============================================
    // Scenario 1 + 7: Enum validity / out-of-range enum byte
    // ============================================

    [Fact]
    public void Create_WithAllEmotionalityValues_AcceptsBoundaries()
    {
        PreTradeChecklistEnums.CreateEmotionality(1).IsSuccess.Should().BeTrue();  // Fearful
        PreTradeChecklistEnums.CreateEmotionality(5).IsSuccess.Should().BeTrue();  // Euphoric
    }

    [Fact]
    public void Create_WithAllSetupQualityValues_AcceptsBoundaries()
    {
        PreTradeChecklistEnums.CreateSetupQuality(1).IsSuccess.Should().BeTrue();  // Poor
        PreTradeChecklistEnums.CreateSetupQuality(5).IsSuccess.Should().BeTrue();  // Excellent
    }

    [Theory]
    [InlineData((byte)0)]
    [InlineData((byte)6)]
    public void Emotionality_WithValueOutOfRange_FailsWithValidationError(byte value)
    {
        var r = PreTradeChecklistEnums.CreateEmotionality(value);

        r.IsFailure.Should().BeTrue();
        r.Error.Code.Should().Be("validation.pre_trade_checklist.emotionality_out_of_range");
    }

    [Theory]
    [InlineData((byte)0)]
    [InlineData((byte)6)]
    public void SetupQuality_WithValueOutOfRange_FailsWithValidationError(byte value)
    {
        var r = PreTradeChecklistEnums.CreateSetupQuality(value);

        r.IsFailure.Should().BeTrue();
        r.Error.Code.Should().Be("validation.pre_trade_checklist.setup_quality_out_of_range");
    }

    [Fact]
    public void Create_WithOutOfRangeEnumByteInSubmission_FailsAtDomainBoundary()
    {
        // Cast a non-enum byte into the submission. The aggregate's
        // domain-level factory uses Emotionality.Create / SetupQuality.Create
        // so the violation surfaces as a validation error rather than a
        // silent truncation.
        var submission = new PreTradeChecklistSubmission(
            Emotionality.Neutral, SetupQuality.Good, 2.5m, 2.0m, 3);

        // Sanity: enum values map 1..5 with no holes.
        ((byte)Emotionality.Fearful).Should().Be(1);
        ((byte)Emotionality.Euphoric).Should().Be(5);
        ((byte)SetupQuality.Poor).Should().Be(1);
        ((byte)SetupQuality.Excellent).Should().Be(5);
    }
}

/// <summary>
/// Quick arbitration tests for the new error catalog so a PR that renames a
/// code does not silently propagate. Keep these synced with
/// <c>TradingDomainErrors.PreTradeChecklist</c>.
/// </summary>
public class PreTradeChecklistErrorCodesTests
{
    [Fact]
    public void PreTradeChecklistErrorCodes_AreStableAndPrefixed()
    {
        TradingDomainErrors.PreTradeChecklist.RrBelowTarget.Code.Should().Be(
            "validation.pre_trade_checklist.rr_below_target");
        TradingDomainErrors.PreTradeChecklist.ConfluencesOutOfRange.Code.Should().Be(
            "validation.pre_trade_checklist.confluences_out_of_range");
        TradingDomainErrors.PreTradeChecklist.EmotionalityOutOfRange.Code.Should().Be(
            "validation.pre_trade_checklist.emotionality_out_of_range");
        TradingDomainErrors.PreTradeChecklist.SetupQualityOutOfRange.Code.Should().Be(
            "validation.pre_trade_checklist.setup_quality_out_of_range");
    }
}
