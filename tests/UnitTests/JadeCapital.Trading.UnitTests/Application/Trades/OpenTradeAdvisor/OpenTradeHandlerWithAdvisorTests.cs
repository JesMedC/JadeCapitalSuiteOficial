using FluentAssertions;
using JadeCapital.Identity.Contracts.Projections;
using JadeCapital.Shared.Kernel.Results;
using JadeCapital.Trading.Application.Ai;
using JadeCapital.Trading.Application.Abstractions;
using JadeCapital.Trading.Application.Features.Trades.OpenTrade;
using JadeCapital.Trading.Domain.Ai;
using JadeCapital.Trading.Domain.Enums;
using JadeCapital.Trading.Domain.PreTradeChecklists;
using Microsoft.Extensions.Logging.Abstractions;

namespace JadeCapital.Trading.UnitTests.Application.Trades.OpenTradeAdvisor;

// ============================================================================
//  OpenTradeHandlerWithAdvisorTests — slice 5c.1 (Wave 5, AI Risk Advisor Pre-Trade).
//
//  Tests the OpenTradeHandler's interaction with IAIRiskAdvisor on the
//  critical path. The advisor is registered as an OPTIONAL dependency — the
//  handler invokes it ONLY when (a) a checklist is present AND (b) the
//  advisor is non-null.
//
//  Pinned contract:
//   - No advisor (null) + checklist present → legacy path unchanged.
//   - No checklist → advisor never invoked.
//   - Advisor returns Warning → trade opens + advisory attached to checklist.
//   - Advisor returns Block → 422 with ai_risk.blocked error.
//   - Advisor returns Failure → silent fallback (trade opens, no advisory).
//   - Advisor throws → silent fallback (trade opens, no advisory).
//   - Existing OpenTradeHandlerTests (no advisor ctor arg) keep working.
// ============================================================================

public class OpenTradeHandlerWithAdvisorTests
{
    private readonly ITradeRepository _trades = Substitute.For<ITradeRepository>();
    private readonly IAccountRepository _accounts = Substitute.For<IAccountRepository>();
    private readonly IInstrumentRepository _instruments = Substitute.For<IInstrumentRepository>();
    private readonly IPreTradeChecklistRepository _checklists = Substitute.For<IPreTradeChecklistRepository>();
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();
    private readonly IClock _clock = Substitute.For<IClock>();
    private readonly IIdentityUserRiskProfileReader _riskProfileReader = Substitute.For<IIdentityUserRiskProfileReader>();
    private readonly IAIRiskAdvisor _advisor = Substitute.For<IAIRiskAdvisor>();

    private static readonly DateTimeOffset FixedNow =
        new(2026, 8, 19, 14, 32, 0, TimeSpan.Zero);

    private OpenTradeHandler CreateSut() => new(
        _trades, _accounts, _instruments, _checklists, _uow, _clock,
        _riskProfileReader, NullLogger<OpenTradeHandler>.Instance, _advisor);

    private OpenTradeHandler CreateSutWithoutAdvisor() => new(
        _trades, _accounts, _instruments, _checklists, _uow, _clock,
        _riskProfileReader, NullLogger<OpenTradeHandler>.Instance);

    private static OpenTradeCommand ValidCommand(PreTradeChecklistSubmissionInput? checklist = null) => new(
        UserId: Guid.NewGuid(),
        AccountId: Guid.NewGuid(),
        InstrumentId: Guid.NewGuid(),
        Symbol: "EURUSD",
        AssetClass: AssetClass.Forex,
        Direction: TradeDirection.Long,
        Volume: 1000m,
        VolumeCurrency: "USD",
        EntryPrice: 1.10m,
        EntryPriceCurrency: "USD",
        Strategy: "trend-following",
        Notes: "Entry at breakout",
        Checklist: checklist);

    private static PreTradeChecklistSubmissionInput ValidChecklist() =>
        new(Emotionality.Neutral, SetupQuality.Good, 2.5m, 2.0m, 3);

    private static AIRiskAdvice RehydrateAdvice(
        AIRiskAction action,
        string reason,
        Guid userId,
        Guid tradeId)
    {
        return AIRiskAdvice.Rehydrate(
            id: Guid.NewGuid(),
            userId: userId,
            tradeId: tradeId,
            contextJson: "{}",
            providerResponseText: $"{{\"action\":\"{action.ToString().ToLowerInvariant()}\"}}",
            parsedAction: action,
            reason: reason,
            model: "llama3.1:8b",
            latencyMs: 412,
            createdAt: FixedNow);
    }

    [Fact]
    public async Task Handle_NoAdvisor_LegacyPath_Unchanged()
    {
        // The advisor is null. The handler never invokes it. The trade opens
        // on the legacy path even with a checklist.
        _clock.UtcNow.Returns(FixedNow);
        _uow.SaveChangesAsync(Arg.Any<CancellationToken>()).Returns(Result.Success(1));

        var cmd = ValidCommand(ValidChecklist());
        var result = await CreateSutWithoutAdvisor().Handle(cmd, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        await _advisor.DidNotReceiveWithAnyArgs().AdviseAsync(default!, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_NoChecklist_Advisor_Not_Invoked()
    {
        // No checklist + advisor registered → advisor never invoked (the
        // pre-trade advisory is gated on the checklist presence).
        _clock.UtcNow.Returns(FixedNow);
        _uow.SaveChangesAsync(Arg.Any<CancellationToken>()).Returns(Result.Success(1));

        var cmd = ValidCommand(checklist: null);
        var result = await CreateSut().Handle(cmd, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        await _advisor.DidNotReceiveWithAnyArgs().AdviseAsync(default!, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_AdvisorWarning_TradeOpens_And_AdvisoryAttached()
    {
        _clock.UtcNow.Returns(FixedNow);
        _uow.SaveChangesAsync(Arg.Any<CancellationToken>()).Returns(Result.Success(1));

        var cmd = ValidCommand(ValidChecklist());

        // The handler will invoke the advisor with a TradeId; we capture the
        // userid from the request to build a matching Rehydrate response.
        Guid capturedUserId = Guid.Empty;
        Guid? capturedTradeId = null;
        _advisor.AdviseAsync(Arg.Do<AIRiskAdviceRequest>(r =>
        {
            capturedUserId = r.UserId;
            capturedTradeId = r.TradeId;
        }), Arg.Any<CancellationToken>())
            .Returns(Result.Success(RehydrateAdvice(AIRiskAction.Warning, "4 losses in a row", capturedUserId, capturedTradeId ?? Guid.Empty)));

        var result = await CreateSut().Handle(cmd, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        capturedUserId.Should().NotBe(Guid.Empty);
        capturedTradeId.Should().NotBeNull();
        // The checklist row was persisted with the advisory attached.
        await _checklists.Received(1).AddAsync(
            Arg.Is<PreTradeChecklist>(c => c.AIRiskAdvisoryJson != null && c.AIRiskAdvisoryJson.Contains("warning")),
            Arg.Any<CancellationToken>());
        await _trades.Received(1).AddAsync(Arg.Any<JadeCapital.Trading.Domain.Trades.Trade>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_AdvisorBlock_Returns_422_ai_risk_blocked()
    {
        _clock.UtcNow.Returns(FixedNow);
        _uow.SaveChangesAsync(Arg.Any<CancellationToken>()).Returns(Result.Success(1));

        var cmd = ValidCommand(ValidChecklist());
        Guid capturedUserId = Guid.Empty;
        Guid? capturedTradeId = null;
        _advisor.AdviseAsync(Arg.Do<AIRiskAdviceRequest>(r =>
        {
            capturedUserId = r.UserId;
            capturedTradeId = r.TradeId;
        }), Arg.Any<CancellationToken>())
            .Returns(Result.Success(RehydrateAdvice(AIRiskAction.Block, "excessive risk", capturedUserId, capturedTradeId ?? Guid.Empty)));

        var result = await CreateSut().Handle(cmd, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("failure.ai_risk.blocked");
        result.Error.Message.Should().Contain("excessive risk");
        // The trade is NOT persisted — short-circuit before UoW.
        await _trades.DidNotReceiveWithAnyArgs().AddAsync(default!, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_AdvisorFailure_TradeOpens_NoAdvisoryAttached()
    {
        _clock.UtcNow.Returns(FixedNow);
        _uow.SaveChangesAsync(Arg.Any<CancellationToken>()).Returns(Result.Success(1));

        var cmd = ValidCommand(ValidChecklist());
        _advisor.AdviseAsync(Arg.Any<AIRiskAdviceRequest>(), Arg.Any<CancellationToken>())
            .Returns(Result.Failure<AIRiskAdvice>(Error.Failure("ai.unavailable", "Ollama down")));

        var result = await CreateSut().Handle(cmd, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        // Checklist still persisted but WITHOUT an advisory attached.
        await _checklists.Received(1).AddAsync(
            Arg.Is<PreTradeChecklist>(c => c.AIRiskAdvisoryJson == null),
            Arg.Any<CancellationToken>());
        await _trades.Received(1).AddAsync(Arg.Any<JadeCapital.Trading.Domain.Trades.Trade>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_AdvisorThrows_TradeOpens_SilentFallback()
    {
        // Defense in depth — the IAIRiskAdvisor contract is no-throw, but if
        // an unexpected exception escapes, the handler catches it and falls
        // back to the legacy path.
        _clock.UtcNow.Returns(FixedNow);
        _uow.SaveChangesAsync(Arg.Any<CancellationToken>()).Returns(Result.Success(1));

        var cmd = ValidCommand(ValidChecklist());
        _advisor
            .AdviseAsync(Arg.Any<AIRiskAdviceRequest>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException<Result<AIRiskAdvice>>(new InvalidOperationException("unexpected advisor blow-up")));

        var result = await CreateSut().Handle(cmd, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        await _trades.Received(1).AddAsync(Arg.Any<JadeCapital.Trading.Domain.Trades.Trade>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_AdvisorAllow_TradeOpens_AdvisoryAttached_With_Allow()
    {
        _clock.UtcNow.Returns(FixedNow);
        _uow.SaveChangesAsync(Arg.Any<CancellationToken>()).Returns(Result.Success(1));

        var cmd = ValidCommand(ValidChecklist());
        Guid capturedUserId = Guid.Empty;
        Guid? capturedTradeId = null;
        _advisor.AdviseAsync(Arg.Do<AIRiskAdviceRequest>(r =>
        {
            capturedUserId = r.UserId;
            capturedTradeId = r.TradeId;
        }), Arg.Any<CancellationToken>())
            .Returns(Result.Success(RehydrateAdvice(AIRiskAction.Allow, "trade looks fine", capturedUserId, capturedTradeId ?? Guid.Empty)));

        var result = await CreateSut().Handle(cmd, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        await _checklists.Received(1).AddAsync(
            Arg.Is<PreTradeChecklist>(c => c.AIRiskAdvisoryJson != null && c.AIRiskAdvisoryJson.Contains("allow")),
            Arg.Any<CancellationToken>());
    }
}
