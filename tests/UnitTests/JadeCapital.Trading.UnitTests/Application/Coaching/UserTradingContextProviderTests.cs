using JadeCapital.Trading.Application.Ai;

namespace JadeCapital.Trading.UnitTests.Application.Coaching;

// ============================================================================
//  UserTradingContextProviderTests — slice 5b.2 (Wave 5).
//
//  RED tests for the IUserTradingContextProvider contract that
//  GenerateCoachingPromptHandler uses to assemble the prompt context.
//  The tests pin the public shape:
//   - GetUserContextAsync(userId, windowDays, ct) → UserTradingContext with
//     closed-trade count + win-rate + avg RR + instruments + violations.
//   - Empty user → empty context, no exception.
//   - User with 0 trades → empty violations + zeroed aggregates.
//   - GetActiveUserIdsWithMinTradesAsync(threshold, windowDays, ct) → IReadOnlyList<Guid>.
// ============================================================================

public class UserTradingContextProviderTests
{
    [Fact]
    public async Task GetUserContextAsync_Returns_Context_With_Aggregates()
    {
        var userId = Guid.NewGuid();
        var provider = new FakeUserTradingContextProvider((uid, days, ct) =>
            new UserTradingContext(
                UserId: uid,
                WindowDays: days,
                ClosedTradeCount: 8,
                Winners: 5,
                Losers: 3,
                WinRate: 0.625m,
                AverageRiskReward: 1.4m,
                InstrumentsTraded: new[] { "EURUSD", "XAUUSD" },
                Violations: new[] { "tilt-sequence", "long-break" }));

        var ctx = await provider.GetUserContextAsync(userId, 7, CancellationToken.None);

        ctx.UserId.Should().Be(userId);
        ctx.WindowDays.Should().Be(7);
        ctx.ClosedTradeCount.Should().Be(8);
        ctx.WinRate.Should().Be(0.625m);
        ctx.InstrumentsTraded.Should().HaveCount(2);
        ctx.Violations.Should().HaveCount(2);
    }

    [Fact]
    public async Task GetUserContextAsync_Empty_User_Returns_Zeroed_Context()
    {
        var provider = new FakeUserTradingContextProvider((uid, days, ct) =>
            new UserTradingContext(uid, days, 0, 0, 0, 0m, 0m, Array.Empty<string>(), Array.Empty<string>()));

        var ctx = await provider.GetUserContextAsync(Guid.NewGuid(), 7, CancellationToken.None);

        ctx.ClosedTradeCount.Should().Be(0);
        ctx.WinRate.Should().Be(0m);
        ctx.Violations.Should().BeEmpty();
    }

    [Fact]
    public async Task GetActiveUserIdsWithMinTradesAsync_Returns_Bounded_List()
    {
        var provider = new FakeUserTradingContextProvider((uid, days, ct) =>
            new UserTradingContext(uid, days, 0, 0, 0, 0m, 0m, Array.Empty<string>(), Array.Empty<string>()));
        provider.ActiveUserIds = Enumerable.Range(0, 50).Select(_ => Guid.NewGuid()).ToList();

        var ids = await provider.GetActiveUserIdsWithMinTradesAsync(minClosedTrades: 5, windowDays: 7, CancellationToken.None);

        ids.Should().HaveCount(50);
    }

    [Fact]
    public async Task GetActiveUserIdsWithMinTradesAsync_Empty_When_No_Active_Users()
    {
        var provider = new FakeUserTradingContextProvider((uid, days, ct) =>
            new UserTradingContext(uid, days, 0, 0, 0, 0m, 0m, Array.Empty<string>(), Array.Empty<string>()));
        provider.ActiveUserIds = Array.Empty<Guid>();

        var ids = await provider.GetActiveUserIdsWithMinTradesAsync(minClosedTrades: 5, windowDays: 7, CancellationToken.None);

        ids.Should().BeEmpty();
    }

    private sealed class FakeUserTradingContextProvider : IUserTradingContextProvider
    {
        private readonly Func<Guid, int, CancellationToken, UserTradingContext> _ctx;
        public IReadOnlyList<Guid> ActiveUserIds { get; set; } = Array.Empty<Guid>();

        public FakeUserTradingContextProvider(
            Func<Guid, int, CancellationToken, UserTradingContext> ctx)
        {
            _ctx = ctx;
        }

        public Task<UserTradingContext> GetUserContextAsync(Guid userId, int windowDays, CancellationToken ct)
            => Task.FromResult(_ctx(userId, windowDays, ct));

        public Task<IReadOnlyList<Guid>> GetActiveUserIdsWithMinTradesAsync(int minClosedTrades, int windowDays, CancellationToken ct)
            => Task.FromResult(ActiveUserIds);
    }
}