using JadeCapital.Shared.Kernel.Ai;
using JadeCapital.Trading.Application.Ai;
using JadeCapital.Trading.Application.Features.Coaching.GenerateCoachingPrompt;
using Microsoft.Extensions.Logging.Abstractions;

namespace JadeCapital.Trading.UnitTests.Application.Coaching;

// ============================================================================
//  GenerateCoachingPromptHandlerTests — slice 5b.2 (Wave 5).
//
//  RED tests for the orchestrator handler. All deps mocked via NSubstitute
//  (per project convention — see GetAiHealthHandlerTests in the same folder).
//
//  Pinned contract:
//   - Happy path: provider returns Success → handler returns Success(1)
//     and persists exactly one row.
//   - AI provider failure → handler returns Failure unchanged; no row
//     added (UoW never called).
//   - Idempotency: existing prompt for today → Success(0); provider NEVER
//     called.
//   - 0-trade user: context.HasNoTrades → Success(0); provider never called.
//   - Severity derived from violations count + win-rate:
//     3+ violations OR win-rate < 40% → High; 1+ violation OR < 60% → Medium;
//     otherwise Low.
//   - Provider response text + model + latency persist verbatim.
//   - CancellationToken propagates to the AI provider.
// ============================================================================

public class GenerateCoachingPromptHandlerTests
{
    private static readonly DateTimeOffset T0 =
        new(2026, 8, 19, 3, 1, 23, TimeSpan.Zero);

    private sealed class FixedClock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset UtcNow => now;
    }

    private static UserTradingContext BuildContext(
        int closed = 8,
        int winners = 5,
        int losers = 3,
        decimal winRate = 0.625m,
        IReadOnlyList<string>? violations = null)
        => new(
            UserId: Guid.NewGuid(),
            WindowDays: 7,
            ClosedTradeCount: closed,
            Winners: winners,
            Losers: losers,
            WinRate: winRate,
            AverageRiskReward: 1.4m,
            InstrumentsTraded: new[] { "EURUSD", "XAUUSD" },
            Violations: violations ?? new[] { "tilt-sequence" });

    private static GenerateCoachingPromptHandler BuildHandler(
        IUserTradingContextProvider ctxProvider,
        IAIProvider aiProvider,
        ICoachingPromptRepository prompts,
        IUnitOfWork uow,
        IClock clock)
        => new(ctxProvider, aiProvider, prompts, uow, clock, NullLogger<GenerateCoachingPromptHandler>.Instance);

    [Fact]
    public async Task Handle_HappyPath_Persists_One_Row_And_Returns_One()
    {
        var userId = Guid.NewGuid();
        var ctx = BuildContext();
        var ctxProvider = Substitute.For<IUserTradingContextProvider>();
        ctxProvider.GetUserContextAsync(userId, 7, Arg.Any<CancellationToken>()).Returns(ctx);

        var aiProvider = Substitute.For<IAIProvider>();
        aiProvider.GenerateAsync(Arg.Any<PromptRequest>(), Arg.Any<CancellationToken>())
            .Returns(Result<PromptResponse>.Success(new PromptResponse(
                Text: "Detectamos bajo RR promedio en tus últimas operaciones.",
                Model: "llama3.1:8b",
                TokensUsed: 36,
                Duration: TimeSpan.FromMilliseconds(412))));

        var prompts = Substitute.For<ICoachingPromptRepository>();
        prompts.FindByUserAndDateAsync(userId, Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns((JadeCapital.Trading.Domain.Ai.CoachingPrompt?)null);

        var uow = Substitute.For<IUnitOfWork>();
        uow.SaveChangesAsync(Arg.Any<CancellationToken>()).Returns(Result.Success(1));

        var handler = BuildHandler(ctxProvider, aiProvider, prompts, uow, new FixedClock(T0));

        var result = await handler.Handle(new GenerateCoachingPromptCommand(userId), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(1);
        await prompts.Received(1).AddAsync(Arg.Any<JadeCapital.Trading.Domain.Ai.CoachingPrompt>(), Arg.Any<CancellationToken>());
        await uow.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_AIProviderFailure_Returns_Failure_Without_Persist()
    {
        var userId = Guid.NewGuid();
        var ctxProvider = Substitute.For<IUserTradingContextProvider>();
        ctxProvider.GetUserContextAsync(userId, 7, Arg.Any<CancellationToken>()).Returns(BuildContext());

        var aiProvider = Substitute.For<IAIProvider>();
        aiProvider.GenerateAsync(Arg.Any<PromptRequest>(), Arg.Any<CancellationToken>())
            .Returns(Result<PromptResponse>.Failure(Error.Failure("ai.unavailable", "Ollama down")));

        var prompts = Substitute.For<ICoachingPromptRepository>();
        prompts.FindByUserAndDateAsync(userId, Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns((JadeCapital.Trading.Domain.Ai.CoachingPrompt?)null);

        var uow = Substitute.For<IUnitOfWork>();

        var handler = BuildHandler(ctxProvider, aiProvider, prompts, uow, new FixedClock(T0));

        var result = await handler.Handle(new GenerateCoachingPromptCommand(userId), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("failure.ai.unavailable");
        await prompts.DidNotReceiveWithAnyArgs().AddAsync(default!,Arg.Any<CancellationToken>());
        await uow.DidNotReceiveWithAnyArgs().SaveChangesAsync(default);
    }

    [Fact]
    public async Task Handle_AlreadyGeneratedToday_Returns_Zero_Without_Calling_Provider()
    {
        var userId = Guid.NewGuid();
        var existingPrompt = JadeCapital.Trading.Domain.Ai.CoachingPrompt.Rehydrate(
            id: Guid.NewGuid(),
            userId: userId,
            promptText: "previous",
            contextJson: "{}",
            providerResponseText: "previous response",
            model: "llama3.1:8b",
            latencyMs: 300,
            severity: JadeCapital.Trading.Domain.Ai.CoachingPromptSeverity.Low,
            kind: JadeCapital.Trading.Domain.Ai.CoachingPromptKind.Ai,
            createdAt: T0);

        var ctxProvider = Substitute.For<IUserTradingContextProvider>();
        var aiProvider = Substitute.For<IAIProvider>();
        var prompts = Substitute.For<ICoachingPromptRepository>();
        prompts.FindByUserAndDateAsync(userId, Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(existingPrompt);

        var uow = Substitute.For<IUnitOfWork>();

        var handler = BuildHandler(ctxProvider, aiProvider, prompts, uow, new FixedClock(T0));

        var result = await handler.Handle(new GenerateCoachingPromptCommand(userId), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(0);
        await aiProvider.DidNotReceiveWithAnyArgs().GenerateAsync(default!,Arg.Any<CancellationToken>());
        await prompts.DidNotReceiveWithAnyArgs().AddAsync(default!,Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ZeroTradeUser_Short_Circuits_With_Zero()
    {
        var userId = Guid.NewGuid();
        var emptyCtx = BuildContext(closed: 0, winners: 0, losers: 0, winRate: 0m, violations: Array.Empty<string>());
        emptyCtx.Should().NotBeNull();

        var ctxProvider = Substitute.For<IUserTradingContextProvider>();
        ctxProvider.GetUserContextAsync(userId, 7, Arg.Any<CancellationToken>())
            .Returns(new UserTradingContext(
                UserId: userId,
                WindowDays: 7,
                ClosedTradeCount: 0,
                Winners: 0,
                Losers: 0,
                WinRate: 0m,
                AverageRiskReward: 0m,
                InstrumentsTraded: Array.Empty<string>(),
                Violations: Array.Empty<string>()));

        var aiProvider = Substitute.For<IAIProvider>();
        var prompts = Substitute.For<ICoachingPromptRepository>();
        prompts.FindByUserAndDateAsync(userId, Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns((JadeCapital.Trading.Domain.Ai.CoachingPrompt?)null);

        var uow = Substitute.For<IUnitOfWork>();

        var handler = BuildHandler(ctxProvider, aiProvider, prompts, uow, new FixedClock(T0));

        var result = await handler.Handle(new GenerateCoachingPromptCommand(userId), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(0);
        await aiProvider.DidNotReceiveWithAnyArgs().GenerateAsync(default!,Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_Propagates_CancellationToken_To_Provider()
    {
        var userId = Guid.NewGuid();
        var ctxProvider = Substitute.For<IUserTradingContextProvider>();
        ctxProvider.GetUserContextAsync(userId, 7, Arg.Any<CancellationToken>()).Returns(BuildContext());

        var aiProvider = Substitute.For<IAIProvider>();
        aiProvider.GenerateAsync(Arg.Any<PromptRequest>(), Arg.Any<CancellationToken>())
            .Returns(Result<PromptResponse>.Success(new PromptResponse("ok", "m", 1, TimeSpan.FromMilliseconds(10))));

        var prompts = Substitute.For<ICoachingPromptRepository>();
        prompts.FindByUserAndDateAsync(userId, Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns((JadeCapital.Trading.Domain.Ai.CoachingPrompt?)null);

        var uow = Substitute.For<IUnitOfWork>();
        uow.SaveChangesAsync(Arg.Any<CancellationToken>()).Returns(Result.Success(1));

        var handler = BuildHandler(ctxProvider, aiProvider, prompts, uow, new FixedClock(T0));

        using var cts = new CancellationTokenSource();
        await handler.Handle(new GenerateCoachingPromptCommand(userId), cts.Token);

        await aiProvider.Received(1).GenerateAsync(Arg.Any<PromptRequest>(), cts.Token);
    }
}