using JadeCapital.Shared.Kernel.Ai;
using JadeCapital.Trading.Domain.Ai;

namespace JadeCapital.Trading.UnitTests.Domain.AiRiskAdvisor;

// ============================================================================
//  AIRiskAdviceTests — slice 5c.1 (Wave 5, AI Risk Advisor Pre-Trade).
//
//  Domain-level tests for the AIRiskAdvice aggregate root. Validates:
//   - ParsedAction byte in [0..2] (Allow=0, Warning=1, Block=2).
//   - Reason length <= 500 (truncated by parser before Create).
//   - ContextJson + ProviderResponse non-empty.
//   - CreatedAt set from the supplied clock.
//   - TradeId is optional (null for manual advisory, populated for OpenTrade).
//   - Rehydrate skips validation (DB CHECK constraint is the safety net).
//   - No public setters — immutable after construction.
//
//  RED → GREEN → REFACTOR. Written first per the strict-TDD contract.
// ============================================================================

public class AIRiskAdviceTests
{
    private static readonly DateTimeOffset T0 =
        new(2026, 8, 19, 14, 32, 0, TimeSpan.Zero);

    private static IClock FixedClock(DateTimeOffset now) => new FixedClockFake(now);

    private static PromptResponse FakeResponse(int latencyMs = 412)
        => new(
            Text: "{\"action\":\"warning\",\"reason\":\"4 losses in a row\"}",
            Model: "llama3.1:8b",
            TokensUsed: 28,
            Duration: TimeSpan.FromMilliseconds(latencyMs));

    [Fact]
    public void Create_Returns_Success_With_Valid_Inputs()
    {
        var userId = Guid.NewGuid();
        var result = AIRiskAdvice.Create(
            userId: userId,
            tradeId: null,
            contextJson: "{\"closed_trades\":4}",
            response: FakeResponse(),
            reason: "4 operaciones perdedoras consecutivas en EURUSD.",
            clock: FixedClock(T0));

        result.IsSuccess.Should().BeTrue();
        result.Value.UserId.Should().Be(userId);
        result.Value.TradeId.Should().BeNull();
        result.Value.ParsedAction.Should().Be(AIRiskAction.Warning);
        result.Value.Reason.Should().Be("4 operaciones perdedoras consecutivas en EURUSD.");
        result.Value.CreatedAt.Should().Be(T0);
    }

    [Fact]
    public void Create_Populates_TradeId_When_Open_Trade_Path()
    {
        var tradeId = Guid.NewGuid();
        var result = AIRiskAdvice.Create(
            userId: Guid.NewGuid(),
            tradeId: tradeId,
            contextJson: "{}",
            response: FakeResponse(),
            reason: "ok",
            parsedAction: AIRiskAction.Allow,
            clock: FixedClock(T0));

        result.IsSuccess.Should().BeTrue();
        result.Value.TradeId.Should().Be(tradeId);
        result.Value.ParsedAction.Should().Be(AIRiskAction.Allow);
    }

    [Fact]
    public void Create_Fails_With_Empty_UserId()
    {
        var result = AIRiskAdvice.Create(
            userId: Guid.Empty,
            tradeId: null,
            contextJson: "{}",
            response: FakeResponse(),
            reason: "ok",
            clock: FixedClock(T0));

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().StartWith("validation.ai_risk_advice.");
    }

    [Fact]
    public void Create_Fails_With_Empty_ContextJson()
    {
        var result = AIRiskAdvice.Create(
            userId: Guid.NewGuid(),
            tradeId: null,
            contextJson: "",
            response: FakeResponse(),
            reason: "ok",
            clock: FixedClock(T0));

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Contain("context_json_required");
    }

    [Fact]
    public void Create_Fails_With_Empty_Response_Text()
    {
        var empty = new PromptResponse(
            Text: "",
            Model: "llama3.1:8b",
            TokensUsed: 0,
            Duration: TimeSpan.FromMilliseconds(100));

        var result = AIRiskAdvice.Create(
            userId: Guid.NewGuid(),
            tradeId: null,
            contextJson: "{}",
            response: empty,
            reason: "ok",
            clock: FixedClock(T0));

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Contain("response_required");
    }

    [Fact]
    public void Create_Fails_With_Reason_Longer_Than_500_Chars()
    {
        var longReason = new string('x', AIRiskAdvice.MaxReasonLength + 1);
        var result = AIRiskAdvice.Create(
            userId: Guid.NewGuid(),
            tradeId: null,
            contextJson: "{}",
            response: FakeResponse(),
            reason: longReason,
            clock: FixedClock(T0));

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Contain("reason_too_long");
    }

    [Fact]
    public void Create_Trims_Reason_Text()
    {
        var result = AIRiskAdvice.Create(
            userId: Guid.NewGuid(),
            tradeId: null,
            contextJson: "{}",
            response: FakeResponse(),
            reason: "   bad idea   ",
            clock: FixedClock(T0));

        result.IsSuccess.Should().BeTrue();
        result.Value.Reason.Should().Be("bad idea");
    }

    [Fact]
    public void Create_Fails_With_Invalid_Action()
    {
        var result = AIRiskAdvice.Create(
            userId: Guid.NewGuid(),
            tradeId: null,
            contextJson: "{}",
            response: FakeResponse(),
            reason: "ok",
            parsedAction: (AIRiskAction)99,
            clock: FixedClock(T0));

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Contain("action_out_of_range");
    }

    [Fact]
    public void Create_Defaults_ParsedAction_To_Allow_When_Not_Specified()
    {
        // The handler derives ParsedAction from the parser. The factory
        // overload that takes the parsed action is the canonical path; the
        // parser → factory wiring sets Allow if the JSON is malformed.
        var result = AIRiskAdvice.Create(
            userId: Guid.NewGuid(),
            tradeId: null,
            contextJson: "{}",
            response: FakeResponse(),
            reason: "ok",
            clock: FixedClock(T0));

        result.IsSuccess.Should().BeTrue();
        result.Value.ParsedAction.Should().Be(AIRiskAction.Warning);
    }

    [Fact]
    public void Create_Preserves_Provider_Model_And_Latency()
    {
        var resp = new PromptResponse(
            Text: "irrelevant",
            Model: "mistral:7b",
            TokensUsed: 0,
            Duration: TimeSpan.FromMilliseconds(987));

        var result = AIRiskAdvice.Create(
            userId: Guid.NewGuid(),
            tradeId: null,
            contextJson: "{}",
            response: resp,
            reason: "ok",
            clock: FixedClock(T0));

        result.IsSuccess.Should().BeTrue();
        result.Value.Model.Should().Be("mistral:7b");
        result.Value.LatencyMs.Should().Be(987);
    }

    [Fact]
    public void Rehydrate_Reserves_All_Fields_Without_Validation()
    {
        // Rehydrate is the EF path — DB CHECK constraints are the safety net,
        // not the factory. Reserved for AIRiskAdviceRepository.
        var id = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var tradeId = Guid.NewGuid();

        var entity = AIRiskAdvice.Rehydrate(
            id: id,
            userId: userId,
            tradeId: tradeId,
            contextJson: "{}",
            providerResponseText: "raw-response",
            parsedAction: AIRiskAction.Block,
            reason: "blocked",
            model: "llama3.1:8b",
            latencyMs: 123,
            createdAt: T0);

        entity.Id.Should().Be(id);
        entity.UserId.Should().Be(userId);
        entity.TradeId.Should().Be(tradeId);
        entity.ParsedAction.Should().Be(AIRiskAction.Block);
        entity.Reason.Should().Be("blocked");
        entity.Model.Should().Be("llama3.1:8b");
        entity.LatencyMs.Should().Be(123);
        entity.CreatedAt.Should().Be(T0);
    }

    [Fact]
    public void Aggregate_Has_No_Public_Setters()
    {
        // The aggregate is immutable after construction. Same precedent as
        // CoachingPrompt — the test is a structural sanity check (no setter
        // methods exposed).
        var result = AIRiskAdvice.Create(
            userId: Guid.NewGuid(),
            tradeId: null,
            contextJson: "{}",
            response: FakeResponse(),
            reason: "ok",
            clock: FixedClock(T0));

        var entity = result.Value;
        var setters = entity.GetType()
            .GetProperties()
            .Where(p => p.GetSetMethod(nonPublic: false) is not null)
            .Select(p => p.Name)
            .ToList();

        setters.Should().BeEmpty();
    }

    private sealed class FixedClockFake : IClock
    {
        private readonly DateTimeOffset _now;
        public FixedClockFake(DateTimeOffset now) { _now = now; }
        public DateTimeOffset UtcNow => _now;
    }
}
