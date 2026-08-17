using JadeCapital.Shared.Kernel.Ai;
using JadeCapital.Shared.Kernel.Results;
using JadeCapital.Trading.Application.Ai;
using JadeCapital.Trading.Application.Features.Coaching.GenerateCoachingPrompt;
using JadeCapital.Trading.Infrastructure.BackgroundServices;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace JadeCapital.Trading.UnitTests.Infrastructure.BackgroundServices;

// ============================================================================
//  CoachingPromptServiceTests — slice 5b.2 (Wave 5).
//
//  RED tests for the BG service. Uses a real DI container with a real
//  <see cref="GenerateCoachingPromptHandler"/> (its 6 deps are mocked) —
//  same pattern as <c>AlertEvaluationServiceTests</c> in this suite.
//
//  Pinned contract:
//   - RunOnceAsync iterates every active user from the context provider.
//   - One user's failure (Result.Failure) does not abort the loop.
//   - Initial delay computes correctly: 3am UTC + jitter in [0, +30min].
//   - Empty active-user list → returns 0.
//   - Handler throws → loop survives (one user is skipped, others continue).
// ============================================================================

public class CoachingPromptServiceTests
{
    private static readonly DateTimeOffset T0 =
        new(2026, 8, 19, 14, 30, 0, TimeSpan.Zero);

    private sealed class FixedScopeFactory(IServiceProvider inner) : IServiceScopeFactory
    {
        public IServiceScope CreateScope() => new FixedScope(inner);
    }

    private sealed class FixedScope(IServiceProvider inner) : IServiceScope
    {
        public IServiceProvider ServiceProvider => inner;
        public void Dispose() { }
    }

    /// <summary>
    /// Wraps a real <see cref="GenerateCoachingPromptHandler"/> but lets the
    /// test override the per-user behavior (success / failure / throw).
    /// </summary>
    private sealed class TestableHandler : GenerateCoachingPromptHandler
    {
        private readonly GenerateCoachingPromptHandler _inner;
        private readonly Func<Guid, Result<int>>? _behavior;
        private readonly bool _throwOnFirstCall;
        private bool _thrownOnce;

        public TestableHandler(
            GenerateCoachingPromptHandler inner,
            IUserTradingContextProvider ctxProvider,
            IAIProvider aiProvider,
            JadeCapital.Trading.Application.Abstractions.ICoachingPromptRepository prompts,
            JadeCapital.Trading.Application.Abstractions.IUnitOfWork uow,
            JadeCapital.Shared.Kernel.Time.IClock clock,
            Func<Guid, Result<int>>? behavior,
            bool throwOnFirstCall = false)
            : base(ctxProvider, aiProvider, prompts, uow, clock, NullLogger<GenerateCoachingPromptHandler>.Instance)
        {
            _inner = inner;
            _behavior = behavior;
            _throwOnFirstCall = throwOnFirstCall;
        }

        public override async Task<Result<int>> Handle(GenerateCoachingPromptCommand req, CancellationToken ct)
        {
            if (_throwOnFirstCall && !_thrownOnce)
            {
                _thrownOnce = true;
                throw new InvalidOperationException("simulated handler throw");
            }

            if (_behavior is null)
                return await _inner.Handle(req, ct);

            return _behavior(req.UserId);
        }
    }

    /// <summary>
    /// Builds a real <see cref="GenerateCoachingPromptHandler"/> with mocked
    /// deps. The handler can be configured to return Success/Failure/throw
    /// per user via the <paramref name="behavior"/> callback, or throw on
    /// the first call when <paramref name="throwOnFirstCall"/> is true.
    /// </summary>
    private static CoachingPromptService Build(
        IReadOnlyList<Guid> activeUsers,
        Func<Guid, Result<int>>? behavior = null,
        bool throwOnFirstCall = false)
    {
        var ctx = Substitute.For<IUserTradingContextProvider>();
        ctx.GetActiveUserIdsWithMinTradesAsync(Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(activeUsers);
        // Non-empty per-user context (handler short-circuits if 0 trades).
        ctx.GetUserContextAsync(Arg.Any<Guid>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new UserTradingContext(
                UserId: Guid.NewGuid(),
                WindowDays: 7,
                ClosedTradeCount: 8,
                Winners: 5,
                Losers: 3,
                WinRate: 0.625m,
                AverageRiskReward: 1.4m,
                InstrumentsTraded: new[] { "EURUSD" },
                Violations: Array.Empty<string>()));

        var ai = Substitute.For<IAIProvider>();
        ai.GenerateAsync(Arg.Any<PromptRequest>(), Arg.Any<CancellationToken>())
            .Returns(Result<PromptResponse>.Success(new PromptResponse(
                Text: "ok", Model: "llama3.1:8b", TokensUsed: 1, Duration: TimeSpan.FromMilliseconds(10))));

        var prompts = Substitute.For<JadeCapital.Trading.Application.Abstractions.ICoachingPromptRepository>();
        prompts.FindByUserAndDateAsync(Arg.Any<Guid>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns((JadeCapital.Trading.Domain.Ai.CoachingPrompt?)null);

        var uow = Substitute.For<JadeCapital.Trading.Application.Abstractions.IUnitOfWork>();
        uow.SaveChangesAsync(Arg.Any<CancellationToken>()).Returns(Result.Success(1));

        var clock = Substitute.For<JadeCapital.Shared.Kernel.Time.IClock>();
        clock.UtcNow.Returns(new DateTimeOffset(2026, 8, 19, 3, 0, 0, TimeSpan.Zero));

        var inner = new GenerateCoachingPromptHandler(
            ctx, ai, prompts, uow, clock,
            NullLogger<GenerateCoachingPromptHandler>.Instance);

        GenerateCoachingPromptHandler handler = new TestableHandler(
            inner, ctx, ai, prompts, uow, clock, behavior, throwOnFirstCall);

        var services = new ServiceCollection();
        services.AddSingleton(ctx);
        services.AddSingleton<IUserTradingContextProvider>(ctx);
        services.AddSingleton(handler);
        var sp = services.BuildServiceProvider();

        return new CoachingPromptService(
            new FixedScopeFactory(sp),
            NullLogger<CoachingPromptService>.Instance);
    }

    [Fact]
    public async Task RunOnceAsync_Iterates_Every_Active_User()
    {
        var users = new[] { Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid() };
        var service = Build(users);

        var count = await service.RunOnceAsync(CancellationToken.None);

        count.Should().Be(3);
    }

    [Fact]
    public async Task RunOnceAsync_One_User_Failure_Does_Not_Abort_Loop()
    {
        var u1 = Guid.NewGuid();
        var u2 = Guid.NewGuid();
        var u3 = Guid.NewGuid();
        var users = new[] { u1, u2, u3 };
        var service = Build(users, behavior: uid =>
        {
            // Lambda sees each user exactly once. Returning Success(1) for
            // u1 + u3, Failure for u2.
            return uid == u2
                ? Result.Failure<int>(Error.Failure("ai.unavailable", "Ollama down"))
                : Result.Success(1);
        });

        var count = await service.RunOnceAsync(CancellationToken.None);

        // u1 + u3 generated, u2 failed.
        count.Should().Be(2);
    }

    [Fact]
    public async Task RunOnceAsync_Empty_Active_List_Returns_Zero()
    {
        var service = Build(Array.Empty<Guid>());

        var count = await service.RunOnceAsync(CancellationToken.None);

        count.Should().Be(0);
    }

    [Fact]
    public async Task RunOnceAsync_Handler_Throws_Loop_Survives()
    {
        var u1 = Guid.NewGuid();
        var u2 = Guid.NewGuid();
        var users = new[] { u1, u2 };
        // throwOnFirstCall=true: u1 throws; u2 falls through to behavior.
        var service = Build(users, behavior: _ => Result.Success(1), throwOnFirstCall: true);

        var count = await service.RunOnceAsync(CancellationToken.None);

        // u1 threw → 0; u2 succeeded → 1. Net: 1.
        count.Should().Be(1);
    }

    [Fact]
    public void ComputeInitialDelay_Now_At_14h30_Target_3h_Tomorrow()
    {
        var delay = CoachingPromptService.ComputeInitialDelay(
            now: T0,
            target: CoachingPromptService.TargetRunTime,  // 03:00 UTC
            maxJitter: CoachingPromptService.MaxJitter);   // 30 min

        // 14:30 UTC → next 03:00 UTC is in 12h 30min. Plus jitter [0, +30 min].
        delay.TotalHours.Should().BeGreaterThanOrEqualTo(12.5);
        delay.TotalHours.Should().BeLessThanOrEqualTo(13.0);
    }

    [Fact]
    public void ComputeInitialDelay_Now_At_01h00_Target_3h_Same_Day()
    {
        var now = new DateTimeOffset(2026, 8, 19, 1, 0, 0, TimeSpan.Zero);
        var delay = CoachingPromptService.ComputeInitialDelay(
            now: now,
            target: CoachingPromptService.TargetRunTime,
            maxJitter: CoachingPromptService.MaxJitter);

        // 01:00 UTC → next 03:00 UTC is in 2h. Plus jitter [0, +30 min].
        delay.TotalHours.Should().BeGreaterThanOrEqualTo(2.0);
        delay.TotalHours.Should().BeLessThanOrEqualTo(2.5);
    }
}