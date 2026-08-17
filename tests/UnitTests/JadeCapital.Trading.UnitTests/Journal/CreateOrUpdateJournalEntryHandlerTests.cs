using FluentAssertions;
using FluentValidation.TestHelper;
using JadeCapital.Trading.Application.Features.Journal.CreateOrUpdate;

namespace JadeCapital.Trading.UnitTests.Journal;

/// <summary>
/// Application-handler tests for <c>CreateOrUpdateJournalEntryHandler</c>
/// — slice 2a.1 of <c>2026-08-17-trader-journal-core</c>.
///
/// Four scenarios called out in the work-unit breakdown cover: upsert
/// new (no existing entry), upsert existing (calls Update on the repo),
/// validation errors propagating from the domain (e.g. plan too long),
/// and the NothingToSave failure (all fields null/empty).
/// </summary>
public class CreateOrUpdateJournalEntryHandlerTests
{
    private readonly IJournalEntryRepository _repo = Substitute.For<IJournalEntryRepository>();
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();
    private readonly IClock _clock = Substitute.For<IClock>();

    private static readonly DateTimeOffset FixedNow =
        new(2026, 8, 16, 14, 0, 0, TimeSpan.Zero);

    public CreateOrUpdateJournalEntryHandlerTests()
    {
        _clock.UtcNow.Returns(FixedNow);
        _uow.SaveChangesAsync(Arg.Any<CancellationToken>())
            .Returns(Result<int>.Success(1));
    }

    private CreateOrUpdateJournalEntryHandler CreateSut()
        => new(_repo, _uow, _clock);

    private static LocalDate Today() => LocalDate.From(
        new DateTimeOffset(2026, 8, 16, 22, 0, 0, TimeSpan.Zero),
        "America/Argentina/Buenos_Aires");

    private static CreateOrUpdateJournalEntryCommand ValidCmd() => new(
        UserId: Guid.NewGuid(),
        LocalDate: Today(),
        Timezone: "America/Argentina/Buenos_Aires",
        MoodPre: 4,
        MoodDuring: null,
        MoodPost: null,
        PremarketPlan: "Watch DXY reversal at 98.50.",
        PostmarketReflection: null,
        Tags: new[] { "fomo", "good-execution" });

    [Fact]
    public async Task Handle_NoExistingEntry_CreatesAndPersistsNewAggregate()
    {
        var cmd = ValidCmd();
        _repo.GetByUserAndDateAsync(cmd.UserId, cmd.LocalDate, Arg.Any<CancellationToken>())
            .ReturnsNull();

        var result = await CreateSut().Handle(cmd, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        await _repo.Received(1).AddAsync(
            Arg.Is<JournalEntry>(e => e.UserId == cmd.UserId && e.LocalDate == cmd.LocalDate),
            Arg.Any<CancellationToken>());
        await _repo.DidNotReceive().UpdateAsync(Arg.Any<JournalEntry>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ExistingEntry_UpdatesInPlace()
    {
        var cmd = ValidCmd();
        var existing = JournalEntry.CreateOrUpdate(
            cmd.UserId, cmd.LocalDate, "America/Argentina/Buenos_Aires",
            Mood.FromTrusted(2), null, null,
            premarketPlan: "Initial plan",
            postmarketReflection: null,
            tags: new[] { "tag-1" },
            clock: _clock).Value;

        _repo.GetByUserAndDateAsync(cmd.UserId, cmd.LocalDate, Arg.Any<CancellationToken>())
            .Returns(existing);

        var result = await CreateSut().Handle(cmd, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        await _repo.Received(1).UpdateAsync(existing, Arg.Any<CancellationToken>());
        await _repo.DidNotReceive().AddAsync(Arg.Any<JournalEntry>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_PremarketPlanOver2000Chars_PropagatesValidationError()
    {
        var cmd = ValidCmd() with { PremarketPlan = new string('x', 2001) };

        var result = await CreateSut().Handle(cmd, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("validation.journal.premarket_plan_too_long");
        await _repo.DidNotReceive().AddAsync(Arg.Any<JournalEntry>(), Arg.Any<CancellationToken>());
        await _repo.DidNotReceive().UpdateAsync(Arg.Any<JournalEntry>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_AllFieldsNullOrEmpty_ReturnsNothingToSaveFailure()
    {
        var cmd = new CreateOrUpdateJournalEntryCommand(
            UserId: Guid.NewGuid(),
            LocalDate: Today(),
            Timezone: "UTC",
            MoodPre: null, MoodDuring: null, MoodPost: null,
            PremarketPlan: null, PostmarketReflection: null,
            Tags: null);

        var result = await CreateSut().Handle(cmd, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("validation.journal.nothing_to_save");
    }

    [Fact]
    public void Validator_RejectsMoodOutOfRange()
    {
        var cmd = ValidCmd() with { MoodPre = 7 };

        var validator = new CreateOrUpdateJournalEntryValidator();
        var result = validator.TestValidate(cmd);

        result.ShouldHaveValidationErrorFor(x => x.MoodPre);
    }

    [Fact]
    public void Validator_RejectsPremarketPlanOver2000Chars()
    {
        var cmd = ValidCmd() with { PremarketPlan = new string('x', 2001) };

        var validator = new CreateOrUpdateJournalEntryValidator();
        var result = validator.TestValidate(cmd);

        result.ShouldHaveValidationErrorFor(x => x.PremarketPlan);
    }

    [Fact]
    public void Validator_RejectsPostmarketReflectionOver5000Chars()
    {
        var cmd = ValidCmd() with { PostmarketReflection = new string('x', 5001) };

        var validator = new CreateOrUpdateJournalEntryValidator();
        var result = validator.TestValidate(cmd);

        result.ShouldHaveValidationErrorFor(x => x.PostmarketReflection);
    }

    [Fact]
    public void Validator_RejectsMoreThan10Tags()
    {
        var cmd = ValidCmd() with { Tags = Enumerable.Range(0, 11).Select(i => $"tag-{i}").ToArray() };

        var validator = new CreateOrUpdateJournalEntryValidator();
        var result = validator.TestValidate(cmd);

        result.ShouldHaveValidationErrorFor(x => x.Tags);
    }

    [Fact]
    public void Validator_RejectsTagOver32Chars()
    {
        var cmd = ValidCmd() with { Tags = new[] { "ok", new string('a', 33) } };

        var validator = new CreateOrUpdateJournalEntryValidator();
        var result = validator.TestValidate(cmd);

        result.ShouldHaveValidationErrorFor(x => x.Tags);
    }
}
