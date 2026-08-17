using FluentAssertions;
using JadeCapital.Trading.Application.Features.Journal.GetToday;

namespace JadeCapital.Trading.UnitTests.Journal;

/// <summary>
/// Application-handler tests for <c>GetTodayJournalEntryHandler</c> —
/// slice 2a.1 of <c>2026-08-17-trader-journal-core</c>.
///
/// Two scenarios: returns the entry DTO when the user has a journal for
/// today, returns a NotFound failure when they don't.
/// </summary>
public class GetTodayJournalEntryQueryTests
{
    private readonly IJournalEntryRepository _repo = Substitute.For<IJournalEntryRepository>();
    private readonly IClock _clock = Substitute.For<IClock>();

    private static readonly DateTimeOffset FixedNow =
        new(2026, 8, 16, 22, 0, 0, TimeSpan.Zero);

    public GetTodayJournalEntryQueryTests()
    {
        _clock.UtcNow.Returns(FixedNow);
    }

    private GetTodayJournalEntryHandler CreateSut()
        => new(_repo, _clock);

    [Fact]
    public async Task Handle_EntryExistsForToday_ReturnsSuccessWithDto()
    {
        var userId = Guid.NewGuid();
        var timezone = "America/Argentina/Buenos_Aires";
        var expected = JournalEntry.CreateOrUpdate(
            userId,
            LocalDate.From(FixedNow, timezone),
            timezone,
            Mood.FromTrusted(4), null, null,
            premarketPlan: "Watch DXY at 98.50.",
            postmarketReflection: null,
            tags: null,
            clock: _clock).Value;

        _repo.GetByUserAndDateAsync(userId, Arg.Any<LocalDate>(), Arg.Any<CancellationToken>())
            .Returns(expected);

        var query = new GetTodayJournalEntryQuery(userId, timezone);
        var result = await CreateSut().Handle(query, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Id.Should().Be(expected.Id);
        result.Value.MoodPre.Should().Be((byte)4);
        result.Value.PremarketPlan.Should().Be("Watch DXY at 98.50.");
    }

    [Fact]
    public async Task Handle_NoEntryForToday_ReturnsNotFoundFailure()
    {
        var query = new GetTodayJournalEntryQuery(Guid.NewGuid(), "UTC");
        _repo.GetByUserAndDateAsync(query.UserId, Arg.Any<LocalDate>(), Arg.Any<CancellationToken>())
            .ReturnsNull();

        var result = await CreateSut().Handle(query, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("notfound.journal.not_found");
    }
}
