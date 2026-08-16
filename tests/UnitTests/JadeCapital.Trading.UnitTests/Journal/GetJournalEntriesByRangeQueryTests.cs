using FluentAssertions;
using JadeCapital.Trading.Application.Features.Journal.GetByRange;

namespace JadeCapital.Trading.UnitTests.Journal;

/// <summary>
/// Application-handler tests for <c>GetJournalEntriesByRangeHandler</c> —
/// slice 2a.1 of <c>2026-08-17-trader-journal-core</c>.
///
/// Two scenarios: returns only entries inside the requested range (with
/// the boundary inclusive), excludes entries outside the range.
/// </summary>
public class GetJournalEntriesByRangeQueryTests
{
    private readonly IJournalEntryRepository _repo = Substitute.For<IJournalEntryRepository>();
    private readonly IClock _clock = Substitute.For<IClock>();

    private static readonly DateTimeOffset FixedNow =
        new(2026, 8, 16, 14, 0, 0, TimeSpan.Zero);

    public GetJournalEntriesByRangeQueryTests()
    {
        _clock.UtcNow.Returns(FixedNow);
    }

    private GetJournalEntriesByRangeHandler CreateSut()
        => new(_repo);

    [Fact]
    public async Task Handle_ReturnsOnlyEntriesInsideRange()
    {
        var userId = Guid.NewGuid();
        var from = new LocalDate(2026, 8, 1);
        var to = new LocalDate(2026, 8, 31);

        var insideA = JournalEntry.CreateOrUpdate(
            userId, new LocalDate(2026, 8, 1), "UTC",
            Mood.FromTrusted(3), null, null, "Aug 1 plan", null, null, _clock).Value;
        var insideB = JournalEntry.CreateOrUpdate(
            userId, new LocalDate(2026, 8, 15), "UTC",
            Mood.FromTrusted(4), null, null, "Aug 15 plan", null, null, _clock).Value;
        var insideC = JournalEntry.CreateOrUpdate(
            userId, new LocalDate(2026, 8, 31), "UTC",
            Mood.FromTrusted(5), null, null, "Aug 31 plan", null, null, _clock).Value;

        var allForUser = new List<JournalEntry> { insideA, insideB, insideC };

        _repo.ListByRangeAsync(userId, from, to, Arg.Any<CancellationToken>())
            .Returns(allForUser);

        var query = new GetJournalEntriesByRangeQuery(userId, from, to);
        var result = await CreateSut().Handle(query, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().HaveCount(3);
        result.Value.Select(d => d.LocalDate).Should().BeEquivalentTo(new[]
        {
            "2026-08-01", "2026-08-15", "2026-08-31",
        });
    }

    [Fact]
    public async Task Handle_NoEntriesInRange_ReturnsEmptyList()
    {
        var userId = Guid.NewGuid();
        var from = new LocalDate(2026, 9, 1);
        var to = new LocalDate(2026, 9, 30);

        _repo.ListByRangeAsync(userId, from, to, Arg.Any<CancellationToken>())
            .Returns(new List<JournalEntry>());

        var query = new GetJournalEntriesByRangeQuery(userId, from, to);
        var result = await CreateSut().Handle(query, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeEmpty();
    }
}
