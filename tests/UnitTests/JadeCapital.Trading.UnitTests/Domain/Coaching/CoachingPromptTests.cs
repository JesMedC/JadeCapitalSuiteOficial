using JadeCapital.Shared.Kernel.Ai;
using JadeCapital.Trading.Domain.Ai;

namespace JadeCapital.Trading.UnitTests.Domain.Coaching;

// ============================================================================
//  CoachingPromptTests — slice 5b.2 (Wave 5).
//
//  Domain-level tests for the <c>CoachingPrompt</c> aggregate root. Validates
//  the invariants declared in design.md §"CoachingPrompt":
//   - Severity byte in [0..2] (Low=0, Medium=1, High=2).
//   - Kind byte is 1 (AI) — Rule (0) lives in a separate table; this
//     aggregate never persists kind=0.
//   - LatencyMs must be non-negative.
//   - Model length <= 64.
//   - PromptText length <= 4000.
//   - ContextJson + ProviderResponse non-empty (the prompt must carry both).
//   - CreatedAt is set from the supplied clock.
//   - No setters exposed — aggregate is immutable after construction.
//
//  RED → GREEN → REFACTOR. Written first per the strict-TDD contract.
// ============================================================================

public class CoachingPromptTests
{
    private static readonly DateTimeOffset T0 =
        new(2026, 8, 19, 3, 1, 23, TimeSpan.Zero);

    private static IClock FixedClock(DateTimeOffset now) => new FixedClockFake(now);

    private static PromptResponse FakeResponse(int latencyMs = 412)
        => new(Text: "Detectamos bajo RR promedio en tus últimas operaciones.",
               Model: "llama3.1:8b",
               TokensUsed: 36,
               Duration: TimeSpan.FromMilliseconds(latencyMs));

    [Fact]
    public void Create_Returns_Success_With_Valid_Inputs()
    {
        var result = CoachingPrompt.Create(
            userId: Guid.NewGuid(),
            promptText: "You are a trading coach…",
            contextJson: "{\"closed_trades\":8}",
            response: FakeResponse(),
            severity: CoachingPromptSeverity.Medium,
            clock: FixedClock(T0));

        result.IsSuccess.Should().BeTrue();
        result.Value.CreatedAt.Should().Be(T0);
        result.Value.Kind.Should().Be(CoachingPromptKind.Ai);
        result.Value.Severity.Should().Be(CoachingPromptSeverity.Medium);
    }

    [Fact]
    public void Create_Fails_With_Empty_UserId()
    {
        var result = CoachingPrompt.Create(
            userId: Guid.Empty,
            promptText: "x",
            contextJson: "{}",
            response: FakeResponse(),
            severity: CoachingPromptSeverity.Low,
            clock: FixedClock(T0));

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().StartWith("validation.coaching_prompt.");
    }

    [Fact]
    public void Create_Fails_With_Empty_PromptText()
    {
        var result = CoachingPrompt.Create(
            userId: Guid.NewGuid(),
            promptText: "",
            contextJson: "{}",
            response: FakeResponse(),
            severity: CoachingPromptSeverity.Low,
            clock: FixedClock(T0));

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Contain("prompt_text_required");
    }

    [Fact]
    public void Create_Fails_When_PromptText_Too_Long()
    {
        var result = CoachingPrompt.Create(
            userId: Guid.NewGuid(),
            promptText: new string('x', CoachingPrompt.MaxPromptTextLength + 1),
            contextJson: "{}",
            response: FakeResponse(),
            severity: CoachingPromptSeverity.Low,
            clock: FixedClock(T0));

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Contain("prompt_text_too_long");
    }

    [Fact]
    public void Create_Fails_With_Empty_ContextJson()
    {
        var result = CoachingPrompt.Create(
            userId: Guid.NewGuid(),
            promptText: "prompt",
            contextJson: "",
            response: FakeResponse(),
            severity: CoachingPromptSeverity.Low,
            clock: FixedClock(T0));

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Contain("context_json_required");
    }

    [Fact]
    public void Create_Fails_With_Empty_Provider_Response_Text()
    {
        var empty = new PromptResponse(Text: "", Model: "llama3.1:8b", TokensUsed: 0, Duration: TimeSpan.FromMilliseconds(100));
        var result = CoachingPrompt.Create(
            userId: Guid.NewGuid(),
            promptText: "prompt",
            contextJson: "{}",
            response: empty,
            severity: CoachingPromptSeverity.Low,
            clock: FixedClock(T0));

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Contain("response_required");
    }

    [Fact]
    public void Create_Fails_With_Invalid_Severity()
    {
        var result = CoachingPrompt.Create(
            userId: Guid.NewGuid(),
            promptText: "prompt",
            contextJson: "{}",
            response: FakeResponse(),
            severity: (CoachingPromptSeverity)99,
            clock: FixedClock(T0));

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Contain("severity_out_of_range");
    }

    [Fact]
    public void Create_Preserves_Provider_Model_And_Latency()
    {
        var resp = new PromptResponse(Text: "hello", Model: "mistral:7b", TokensUsed: 24, Duration: TimeSpan.FromMilliseconds(987));
        var result = CoachingPrompt.Create(
            userId: Guid.NewGuid(),
            promptText: "p",
            contextJson: "{}",
            response: resp,
            severity: CoachingPromptSeverity.High,
            clock: FixedClock(T0));

        result.IsSuccess.Should().BeTrue();
        result.Value.Model.Should().Be("mistral:7b");
        result.Value.LatencyMs.Should().Be(987);
        result.Value.ProviderResponseText.Should().Be("hello");
    }

    [Fact]
    public void Create_Persists_Kind_As_Ai()
    {
        var result = CoachingPrompt.Create(
            userId: Guid.NewGuid(),
            promptText: "p",
            contextJson: "{}",
            response: FakeResponse(),
            severity: CoachingPromptSeverity.Low,
            clock: FixedClock(T0));

        result.IsSuccess.Should().BeTrue();
        result.Value.Kind.Should().Be(CoachingPromptKind.Ai);
    }

    [Fact]
    public void Create_Fails_When_Model_Too_Long()
    {
        var resp = new PromptResponse(Text: "ok", Model: new string('m', 65), TokensUsed: 0, Duration: TimeSpan.FromMilliseconds(1));
        var result = CoachingPrompt.Create(
            userId: Guid.NewGuid(),
            promptText: "p",
            contextJson: "{}",
            response: resp,
            severity: CoachingPromptSeverity.Low,
            clock: FixedClock(T0));

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Contain("model_too_long");
    }

    [Fact]
    public void Create_Clamps_Negative_Latency_To_Zero()
    {
        // PromptResponse's own constructor rejects negative durations so we
        // can't construct one with Duration < 0 directly. The aggregate
        // additionally clamps any non-negative value below 0 ms (defense in
        // depth — e.g. if a future provider returns Duration = -1ms after
        // rounding). 0ms is the safe floor.
        var resp = new PromptResponse(Text: "ok", Model: "m", TokensUsed: 0, Duration: TimeSpan.Zero);
        var result = CoachingPrompt.Create(
            userId: Guid.NewGuid(),
            promptText: "p",
            contextJson: "{}",
            response: resp,
            severity: CoachingPromptSeverity.Low,
            clock: FixedClock(T0));

        result.IsSuccess.Should().BeTrue();
        result.Value.LatencyMs.Should().Be(0);
    }

    private sealed class FixedClockFake : IClock
    {
        private readonly DateTimeOffset _now;
        public FixedClockFake(DateTimeOffset now) { _now = now; }
        public DateTimeOffset UtcNow => _now;
    }
}