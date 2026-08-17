using JadeCapital.Trading.Application.Alerts;
using AlertWire = JadeCapital.Shared.Kernel.Alerts.Alert;
using Severity = JadeCapital.Shared.Kernel.Coaching.Severity;
using Cta = JadeCapital.Shared.Kernel.Coaching.Cta;
using Microsoft.Extensions.Logging;

namespace JadeCapital.Trading.UnitTests.Alerts;

// ============================================================================
//  AlertEvaluationService tests — slice 3b (Trader Strategies + Alerts + Planner).
//
//  Three scenarios:
//   1. New alert for a rule is persisted (AddAsync returns true).
//   2. Dedup'd insert: AddAsync returns false when a row for (user, rule,
//      UTC-date) already exists — the service counts only the new row.
//   3. Repo throwing is swallowed and the service returns 0 (per spec
//      scenario "Service survives transient errors").
//
//  Uses a FakeAlertRule that emits a single alert on demand so we can
//  drive the registry deterministically.
// ============================================================================

public class AlertEvaluationServiceTests
{
    private readonly IClock _clock = Substitute.For<IClock>();
    private readonly DateTimeOffset _now = new(2026, 8, 18, 14, 0, 0, TimeSpan.Zero);

    private readonly ITradeRepository _trades = Substitute.For<ITradeRepository>();
    private readonly IJournalEntryRepository _journals = Substitute.For<IJournalEntryRepository>();
    private readonly IPreTradeChecklistRepository _checklists = Substitute.For<IPreTradeChecklistRepository>();
    private readonly IAlertRepository _alerts = Substitute.For<IAlertRepository>();

    public AlertEvaluationServiceTests()
    {
        _clock.UtcNow.Returns(_now);
        _trades.ListClosedByUserIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(Array.Empty<Trade>());
        _trades.ListByUserIdAsync(Arg.Any<Guid>(), Arg.Any<int>(), Arg.Any<int>(),
                Arg.Any<CancellationToken>(), Arg.Any<TradeStatus?>(), Arg.Any<string?>(), Arg.Any<Guid?>())
            .Returns(Array.Empty<Trade>());
        _journals.ListByRangeAsync(Arg.Any<Guid>(), Arg.Any<JadeCapital.Shared.Kernel.Time.LocalDate>(),
                Arg.Any<JadeCapital.Shared.Kernel.Time.LocalDate>(), Arg.Any<CancellationToken>())
            .Returns(Array.Empty<JournalEntry>());
        _checklists.ListByUserIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(Array.Empty<PreTradeChecklist>());
    }

    private AlertEvaluationService CreateSut(IReadOnlyList<AlertWire> emitted)
    {
        var rule = new FakeAlertRule("TestRule", priority: 100, alerts: emitted);
        var registry = new AlertRegistry(
            new IAlertRule[] { rule },
            Substitute.For<ILogger<AlertRegistry>>());
        return new AlertEvaluationService(
            registry, _alerts, _trades, _journals, _checklists, _clock,
            Substitute.For<ILogger<AlertEvaluationService>>());
    }

    [Fact]
    public async Task EvaluateForUser_PersistsNewAlert()
    {
        var wire = new AlertWire("TestRule", Severity.Medium,
            "title", "body", new Cta("/x", "x"), null);
        _alerts.AddAsync(Arg.Any<Alert>(), Arg.Any<CancellationToken>()).Returns(true);

        var inserted = await CreateSut(new[] { wire })
            .EvaluateForUserAsync(Guid.NewGuid(), CancellationToken.None);

        inserted.Should().Be(1);
        await _alerts.Received(1).AddAsync(
            Arg.Is<Alert>(a => a.RuleId == "TestRule" && a.Title == "title"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task EvaluateForUser_DedupSkipsInsert()
    {
        var wire = new AlertWire("TestRule", Severity.Medium,
            "title", "body", new Cta("/x", "x"), null);
        // AddAsync returns false → dedup hit on UNIQUE INDEX.
        _alerts.AddAsync(Arg.Any<Alert>(), Arg.Any<CancellationToken>()).Returns(false);

        var inserted = await CreateSut(new[] { wire })
            .EvaluateForUserAsync(Guid.NewGuid(), CancellationToken.None);

        inserted.Should().Be(0);
    }

    [Fact]
    public async Task EvaluateForUser_RepositoryThrows_SwallowsAndReturnsZero()
    {
        var wire = new AlertWire("TestRule", Severity.Medium,
            "title", "body", new Cta("/x", "x"), null);
        // Repository throws during the AddAsync call. The service catches
        // and returns 0 so the BackgroundService can move to the next user.
        _alerts.AddAsync(Arg.Any<Alert>(), Arg.Any<CancellationToken>())
            .Returns<Task<bool>>(_ => throw new InvalidOperationException("simulated repo failure"));

        var inserted = await CreateSut(new[] { wire })
            .EvaluateForUserAsync(Guid.NewGuid(), CancellationToken.None);

        inserted.Should().Be(0);
    }
}