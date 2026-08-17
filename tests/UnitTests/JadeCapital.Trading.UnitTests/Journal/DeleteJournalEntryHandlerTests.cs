using FluentAssertions;
using JadeCapital.Trading.Application.Features.Journal.Delete;

namespace JadeCapital.Trading.UnitTests.Journal;

/// <summary>
/// Application-handler tests for <c>DeleteJournalEntryHandler</c> —
/// slice 2a.1 of <c>2026-08-17-trader-journal-core</c>.
///
/// One combined scenario covering both success (entry exists and belongs
/// to user; DeleteAsync called) and 404 (entry does not exist OR
/// belongs to a different user — collapsed to NotFound for cross-user
/// safety).
/// </summary>
public class DeleteJournalEntryHandlerTests
{
    private readonly IJournalEntryRepository _repo = Substitute.For<IJournalEntryRepository>();
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();

    public DeleteJournalEntryHandlerTests()
    {
        _uow.SaveChangesAsync(Arg.Any<CancellationToken>())
            .Returns(Result<int>.Success(1));
    }

    private DeleteJournalEntryHandler CreateSut()
        => new(_repo, _uow);

    [Fact]
    public async Task Handle_EntryExistsAndBelongsToUser_DeletesAndReturnsSuccess()
    {
        var userId = Guid.NewGuid();
        var entryId = Guid.NewGuid();
        var entry = JournalEntry.Rehydrate(
            id: entryId,
            userId: userId,
            localDate: new LocalDate(2026, 8, 16),
            timezone: "UTC",
            moodPre: null, moodDuring: null, moodPost: null,
            premarketPlan: null, postmarketReflection: null,
            tags: Array.Empty<string>(),
            createdAt: new DateTimeOffset(2026, 8, 16, 12, 0, 0, TimeSpan.Zero),
            updatedAt: new DateTimeOffset(2026, 8, 16, 12, 0, 0, TimeSpan.Zero));

        _repo.FindByIdAsync(entryId, userId, Arg.Any<CancellationToken>())
            .Returns(entry);

        var cmd = new DeleteJournalEntryCommand(entryId, userId);
        var result = await CreateSut().Handle(cmd, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        await _repo.Received(1).DeleteAsync(entryId, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_EntryNotFound_ReturnsNotFoundFailure()
    {
        var userId = Guid.NewGuid();
        var entryId = Guid.NewGuid();
        _repo.FindByIdAsync(entryId, userId, Arg.Any<CancellationToken>())
            .ReturnsNull();

        var cmd = new DeleteJournalEntryCommand(entryId, userId);
        var result = await CreateSut().Handle(cmd, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("notfound.journal.not_found");
        await _repo.DidNotReceive().DeleteAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }
}
