using JadeCapital.Shared.Kernel.Money;
using JadeCapital.Shared.Kernel.Time;
using JadeCapital.Trading.Application.Features.Planner.GetPlannerSessionsByWeek;
using JadeCapital.Trading.Contracts.Planner;
using JadeCapital.Trading.Domain.Accounts;
using JadeCapital.Trading.Domain.Enums;
using JadeCapital.Trading.Domain.Instruments;
using JadeCapital.Trading.Domain.Planner;
using JadeCapital.Trading.Domain.Trades;
using JadeCapital.Trading.Domain.ValueObjects;

namespace JadeCapital.Trading.UnitTests.Planner;

// ============================================================================
//  GetPlannerSessionsByWeekHandlerTests — slice 3c.
//
//  Scenarios:
//   1. List returns sessions for the week in chronological order with
//      comparison payload (followsPlan=true when completed + matching trades).
//   2. followsPlan=false when completed but symbols don't match.
//   3. followsPlan=false when skipped (regardless of trades).
//   4. Invalid week (not Monday) → invalid_week error.
//   5. Empty week returns empty sessions + zero comparison.
// ============================================================================

public class GetPlannerSessionsByWeekHandlerTests
{
    private readonly IPlannerSessionRepository _sessions = Substitute.For<IPlannerSessionRepository>();
    private readonly ITradeRepository _trades = Substitute.For<ITradeRepository>();
    private readonly IClock _clock = Substitute.For<IClock>();

    private GetPlannerSessionsByWeekHandler CreateSut() => new(_sessions, _trades);

    [Fact]
    public async Task Handle_CompletedSessionWithMatchingTrades_FollowsPlanIsTrue()
    {
        var userId = Guid.NewGuid();
        var monday = new LocalDate(2026, 8, 17); // Monday
        var friday = new LocalDate(2026, 8, 21);

        var session = PlannerSession.Create(
            userId,
            sessionDate: friday,
            plannedStartTime: null,
            plannedEndTime: null,
            symbol: "EURUSD",
            notes: null,
            clock: _clock).Value;
        // Mark as completed.
        session.MarkCompleted(_clock);

        _sessions.ListByUserAndWeekAsync(userId, monday, monday.AddDays(6), Arg.Any<CancellationToken>())
            .Returns(new[] { session });
        _trades.ListByUserIdAndOpenedAtRangeAsync(
            userId,
            Arg.Any<DateTimeOffset>(), Arg.Any<DateTimeOffset>(),
            Arg.Any<CancellationToken>())
            .Returns(new List<Trade>
            {
                MakeTrade(userId, friday, "EURUSD", TradeStatus.Closed, 100m),
            });
        _sessions.GetWeekComparisonAsync(userId, monday, monday.AddDays(6), Arg.Any<CancellationToken>())
            .Returns(new PlannerWeekComparisonDto(1, 1, 0, 0, 1, 100m));

        var result = await CreateSut().Handle(
            new GetPlannerSessionsByWeekQuery(userId, monday), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Sessions.Should().HaveCount(1);
        result.Value.Sessions[0].Comparison.FollowsPlan.Should().BeTrue();
        result.Value.Sessions[0].Comparison.ActualTradeCount.Should().Be(1);
        result.Value.Sessions[0].Comparison.ActualClosedTradeCount.Should().Be(1);
        result.Value.Sessions[0].Comparison.ActualSymbols.Should().Contain("EURUSD");
        result.Value.Comparison.Planned.Should().Be(1);
        result.Value.Comparison.Completed.Should().Be(1);
        result.Value.Comparison.ActualTrades.Should().Be(1);
        result.Value.Comparison.TotalPnl.Should().Be(100m);
    }

    [Fact]
    public async Task Handle_CompletedSessionWithMismatchedSymbol_FollowsPlanIsFalse()
    {
        var userId = Guid.NewGuid();
        var monday = new LocalDate(2026, 8, 17);
        var friday = new LocalDate(2026, 8, 21);

        var session = PlannerSession.Create(
            userId,
            sessionDate: friday,
            plannedStartTime: null,
            plannedEndTime: null,
            symbol: "EURUSD",
            notes: null,
            clock: _clock).Value;
        session.MarkCompleted(_clock);

        _sessions.ListByUserAndWeekAsync(userId, monday, monday.AddDays(6), Arg.Any<CancellationToken>())
            .Returns(new[] { session });
        _trades.ListByUserIdAndOpenedAtRangeAsync(
            userId, Arg.Any<DateTimeOffset>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(new List<Trade>
            {
                // GBPUSD trades — different from planned EURUSD.
                MakeTrade(userId, friday, "GBPUSD", TradeStatus.Closed, 50m),
            });
        _sessions.GetWeekComparisonAsync(userId, monday, monday.AddDays(6), Arg.Any<CancellationToken>())
            .Returns(new PlannerWeekComparisonDto(1, 1, 0, 0, 1, 50m));

        var result = await CreateSut().Handle(
            new GetPlannerSessionsByWeekQuery(userId, monday), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Sessions[0].Comparison.FollowsPlan.Should().BeFalse();
    }

    [Fact]
    public async Task Handle_SkippedSessionWithZeroTrades_FollowsPlanIsFalse()
    {
        var userId = Guid.NewGuid();
        var monday = new LocalDate(2026, 8, 17);
        var wednesday = new LocalDate(2026, 8, 19);

        var session = PlannerSession.Create(
            userId,
            sessionDate: wednesday,
            plannedStartTime: null,
            plannedEndTime: null,
            symbol: "EURUSD",
            notes: null,
            clock: _clock).Value;
        session.MarkSkipped(_clock);

        _sessions.ListByUserAndWeekAsync(userId, monday, monday.AddDays(6), Arg.Any<CancellationToken>())
            .Returns(new[] { session });
        _trades.ListByUserIdAndOpenedAtRangeAsync(
            userId, Arg.Any<DateTimeOffset>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(new List<Trade>());
        _sessions.GetWeekComparisonAsync(userId, monday, monday.AddDays(6), Arg.Any<CancellationToken>())
            .Returns(new PlannerWeekComparisonDto(1, 0, 1, 0, 0, 0m));

        var result = await CreateSut().Handle(
            new GetPlannerSessionsByWeekQuery(userId, monday), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Sessions[0].Comparison.FollowsPlan.Should().BeFalse();
    }

    [Fact]
    public async Task Handle_NotMondayWeekStart_ReturnsInvalidWeekError()
    {
        var userId = Guid.NewGuid();
        var wednesday = new LocalDate(2026, 8, 19); // Wednesday — not Monday.

        var result = await CreateSut().Handle(
            new GetPlannerSessionsByWeekQuery(userId, wednesday), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("validation.planner.invalid_week");
    }

    [Fact]
    public async Task Handle_EmptyWeek_ReturnsEmptySessionsAndZeroComparison()
    {
        var userId = Guid.NewGuid();
        var monday = new LocalDate(2026, 8, 17);

        _sessions.ListByUserAndWeekAsync(userId, monday, monday.AddDays(6), Arg.Any<CancellationToken>())
            .Returns(Array.Empty<PlannerSession>());
        _trades.ListByUserIdAndOpenedAtRangeAsync(
            userId, Arg.Any<DateTimeOffset>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(new List<Trade>());
        _sessions.GetWeekComparisonAsync(userId, monday, monday.AddDays(6), Arg.Any<CancellationToken>())
            .Returns(new PlannerWeekComparisonDto(0, 0, 0, 0, 0, 0m));

        var result = await CreateSut().Handle(
            new GetPlannerSessionsByWeekQuery(userId, monday), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Sessions.Should().BeEmpty();
        result.Value.Comparison.Planned.Should().Be(0);
        result.Value.Comparison.TotalPnl.Should().Be(0m);
    }

    /// <summary>Helper para construir un Trade con simbolo, fecha y status.</summary>
    private static Trade MakeTrade(
        Guid userId, LocalDate date, string symbol, TradeStatus status, decimal pnl)
    {
        var accountResult = Account.Open(
            id: Guid.NewGuid(),
            userId: userId, name: "TestAccount", broker: "TestBroker",
            marketType: MarketType.Forex, currency: "USD",
            initialBalance: decimal.Zero, leverage: 100m,
            clock: new StaticClock(new DateTimeOffset(2026, 8, 17, 10, 0, 0, TimeSpan.Zero)));
        var account = accountResult.Value;

        var instrumentResult = Instrument.Create(
            id: Guid.NewGuid(),
            symbol: symbol,
            assetClasses: AssetClass.Forex,
            contractSize: 100_000m,
            decimalPlaces: 5,
            pipValue: 10m,
            payoutPercent: 0m,
            clock: new StaticClock(new DateTimeOffset(2026, 8, 17, 10, 0, 0, TimeSpan.Zero)));
        var instrument = instrumentResult.Value;

        var openResult = Trade.Open(
            id: Guid.NewGuid(),
            userId: userId,
            accountId: account.Id,
            instrumentId: instrument.Id,
            symbol: Symbol.FromTrusted(symbol),
            assetClass: AssetClass.Forex,
            direction: TradeDirection.Long,
            volume: Money.FromTrusted(1m, "lots"),
            entryPrice: Money.FromTrusted(1.5m, "USD"),
            accountCurrency: "USD",
            strategy: null,
            notes: null,
            openedAt: new DateTimeOffset(date.Year, date.Month, date.Day, 10, 0, 0, TimeSpan.Zero));
        var trade = openResult.Value;

        if (status == TradeStatus.Closed)
        {
            trade.Close(
                exitPrice: Money.FromTrusted(1.6m, "USD"),
                closedAt: new DateTimeOffset(date.Year, date.Month, date.Day, 11, 0, 0, TimeSpan.Zero),
                clock: new StaticClock(new DateTimeOffset(date.Year, date.Month, date.Day, 11, 0, 0, TimeSpan.Zero)));
        }
        else if (status == TradeStatus.Cancelled)
        {
            trade.Cancel(new StaticClock(new DateTimeOffset(date.Year, date.Month, date.Day, 11, 0, 0, TimeSpan.Zero)));
        }

        return trade;
    }

    private sealed class StaticClock : IClock
    {
        private readonly DateTimeOffset _now;
        public StaticClock(DateTimeOffset now) { _now = now; }
        public DateTimeOffset UtcNow => _now;
    }
}