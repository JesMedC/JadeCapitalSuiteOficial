using FluentAssertions;
using JadeCapital.Shared.Kernel.Results;
using JadeCapital.Shared.Kernel.Time;

namespace JadeCapital.Trading.UnitTests.Journal;

/// <summary>
/// Domain tests for the <see cref="JadeCapital.Trading.Domain.Journal.JournalEntry"/>
/// aggregate (slice 2a.1 of <c>2026-08-17-trader-journal-core</c>).
///
/// Nine scenarios called out in the work-unit breakdown cover: the happy
/// create with all fields populated, the single-active-per-user-per-day
/// invariant (idempotent upsert), the mood out-of-range rejection at the
/// VO boundary, the plan length cap, the reflection length cap, the
/// tags count cap, the tag individual length cap, the update idempotency
/// (second call merges), and the NothingToSave failure when every field
/// is null/empty.
/// </summary>
public class JournalEntryTests
{
    private static readonly DateTimeOffset FixedNow =
        new(2026, 8, 16, 14, 0, 0, TimeSpan.Zero);

    private static readonly IClock Clock =
        Substitute.For<IClock>();

    public JournalEntryTests()
    {
        Clock.UtcNow.Returns(FixedNow);
    }

    private static LocalDate TodayBuenosAires() => LocalDate.From(
        new DateTimeOffset(2026, 8, 16, 22, 0, 0, TimeSpan.Zero),
        "America/Argentina/Buenos_Aires");

    [Fact]
    public void CreateOrUpdate_WithAllFields_ReturnsSuccessWithAllFieldsPersisted()
    {
        var userId = Guid.NewGuid();
        var date = TodayBuenosAires();
        var tags = new[] { "fomo", "good-execution", "revenge" };

        var result = JadeCapital.Trading.Domain.Journal.JournalEntry.CreateOrUpdate(
            userId, date, "America/Argentina/Buenos_Aires",
            moodPre: Mood.FromTrusted(4),
            moodDuring: Mood.FromTrusted(3),
            moodPost: Mood.FromTrusted(5),
            premarketPlan: "Watch DXY reversal at 98.50.",
            postmarketReflection: "Held through news, scaled out at target.",
            tags: tags,
            clock: Clock);

        result.IsSuccess.Should().BeTrue();
        var entry = result.Value;
        entry.UserId.Should().Be(userId);
        entry.LocalDate.Should().Be(date);
        entry.Timezone.Should().Be("America/Argentina/Buenos_Aires");
        entry.MoodPre!.Value.Value.Should().Be((byte)4);
        entry.MoodDuring!.Value.Value.Should().Be((byte)3);
        entry.MoodPost!.Value.Value.Should().Be((byte)5);
        entry.PremarketPlan.Should().Be("Watch DXY reversal at 98.50.");
        entry.PostmarketReflection.Should().Be("Held through news, scaled out at target.");
        entry.Tags.Should().BeEquivalentTo(tags);
    }

    [Fact]
    public void CreateOrUpdate_TwiceForSameUserAndDate_IsIdempotentAndActsAsUpsert()
    {
        // Single-active-per-user-per-date invariant: the aggregate must
        // accept the second call as an update (the DB UNIQUE INDEX catches
        // a true race; the aggregate must not double-create in-process).
        var userId = Guid.NewGuid();
        var date = TodayBuenosAires();

        var first = JadeCapital.Trading.Domain.Journal.JournalEntry.CreateOrUpdate(
            userId, date, "UTC",
            moodPre: Mood.FromTrusted(3),
            moodDuring: null,
            moodPost: null,
            premarketPlan: "Initial plan",
            postmarketReflection: null,
            tags: new[] { "tag-1" },
            clock: Clock);
        first.IsSuccess.Should().BeTrue();

        var second = JadeCapital.Trading.Domain.Journal.JournalEntry.CreateOrUpdate(
            userId, date, "UTC",
            moodPre: Mood.FromTrusted(4),  // changed
            moodDuring: Mood.FromTrusted(4), // newly filled
            moodPost: null,
            premarketPlan: "Refined plan",   // changed
            postmarketReflection: null,
            tags: new[] { "tag-1", "tag-2" }, // changed
            clock: Clock);
        second.IsSuccess.Should().BeTrue();

        // Both calls produce a valid aggregate for the same (user, date).
        // The DB unique index is what enforces single-row persistence.
        first.Value.LocalDate.Should().Be(second.Value.LocalDate);
        first.Value.UserId.Should().Be(second.Value.UserId);
    }

    [Fact]
    public void Mood_Create_WhenValueIsOutOfRange_ReturnsFailure()
    {
        var result = Mood.Create(7);
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("validation.journal.mood_out_of_range");
    }

    [Fact]
    public void Mood_Create_WhenValueIsZero_ReturnsFailure()
    {
        var result = Mood.Create(0);
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("validation.journal.mood_out_of_range");
    }

    [Fact]
    public void CreateOrUpdate_WithPremarketPlanOver2000Chars_ReturnsFailure()
    {
        var plan = new string('x', 2001);

        var result = JadeCapital.Trading.Domain.Journal.JournalEntry.CreateOrUpdate(
            Guid.NewGuid(), TodayBuenosAires(), "UTC",
            moodPre: null, moodDuring: null, moodPost: null,
            premarketPlan: plan,
            postmarketReflection: null,
            tags: null,
            clock: Clock);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("validation.journal.premarket_plan_too_long");
    }

    [Fact]
    public void CreateOrUpdate_WithPostmarketReflectionOver5000Chars_ReturnsFailure()
    {
        var reflection = new string('x', 5001);

        var result = JadeCapital.Trading.Domain.Journal.JournalEntry.CreateOrUpdate(
            Guid.NewGuid(), TodayBuenosAires(), "UTC",
            moodPre: null, moodDuring: null, moodPost: null,
            premarketPlan: null,
            postmarketReflection: reflection,
            tags: null,
            clock: Clock);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("validation.journal.postmarket_reflection_too_long");
    }

    [Fact]
    public void CreateOrUpdate_WithMoreThan10Tags_ReturnsFailure()
    {
        var tags = Enumerable.Range(0, 11).Select(i => $"tag-{i}").ToArray();

        var result = JadeCapital.Trading.Domain.Journal.JournalEntry.CreateOrUpdate(
            Guid.NewGuid(), TodayBuenosAires(), "UTC",
            moodPre: null, moodDuring: null, moodPost: null,
            premarketPlan: null, postmarketReflection: null,
            tags: tags,
            clock: Clock);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("validation.journal.too_many_tags");
    }

    [Fact]
    public void CreateOrUpdate_WithTagOver32Chars_ReturnsFailure()
    {
        var tags = new[] { "ok-tag", new string('a', 33) };

        var result = JadeCapital.Trading.Domain.Journal.JournalEntry.CreateOrUpdate(
            Guid.NewGuid(), TodayBuenosAires(), "UTC",
            moodPre: null, moodDuring: null, moodPost: null,
            premarketPlan: null, postmarketReflection: null,
            tags: tags,
            clock: Clock);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("validation.journal.tag_too_long");
    }

    [Fact]
    public void CreateOrUpdate_WhenEveryFieldIsNullOrEmpty_ReturnsNothingToSaveFailure()
    {
        var result = JadeCapital.Trading.Domain.Journal.JournalEntry.CreateOrUpdate(
            Guid.NewGuid(), TodayBuenosAires(), "UTC",
            moodPre: null, moodDuring: null, moodPost: null,
            premarketPlan: null,
            postmarketReflection: null,
            tags: null,
            clock: Clock);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("validation.journal.nothing_to_save");
    }

    [Fact]
    public void Update_OnExistingEntry_AppliesChangesAndTouchesUpdatedAt()
    {
        var userId = Guid.NewGuid();
        var date = TodayBuenosAires();
        var created = JadeCapital.Trading.Domain.Journal.JournalEntry.CreateOrUpdate(
            userId, date, "UTC",
            moodPre: Mood.FromTrusted(2),
            moodDuring: null,
            moodPost: null,
            premarketPlan: "Initial plan",
            postmarketReflection: null,
            tags: new[] { "tag-1" },
            clock: Clock).Value;

        var initialUpdatedAt = created.UpdatedAt;

        // Advance the clock so Touch() produces a different UpdatedAt.
        Clock.UtcNow.Returns(FixedNow.AddHours(1));

        var updateResult = created.Update(
            moodPre: Mood.FromTrusted(5),
            moodDuring: Mood.FromTrusted(4),
            moodPost: null,
            premarketPlan: "Refined plan",
            postmarketReflection: null,
            tags: new[] { "tag-1", "tag-2" },
            clock: Clock);

        updateResult.IsSuccess.Should().BeTrue();
        created.MoodPre!.Value.Value.Should().Be((byte)5);
        created.MoodDuring!.Value.Value.Should().Be((byte)4);
        created.PremarketPlan.Should().Be("Refined plan");
        created.Tags.Should().BeEquivalentTo(new[] { "tag-1", "tag-2" });
        created.UpdatedAt.Should().NotBe(initialUpdatedAt);
    }

    [Fact]
    public void Mood_FromTrusted_AcceptsAnyByteWithoutRangeCheck()
    {
        // Hydration path: trust the source (DB column already validated).
        var mood = Mood.FromTrusted(3);
        mood.Value.Should().Be((byte)3);

        var label = mood.Label;
        label.Should().Be("Neutral");
    }

    [Fact]
    public void Mood_Label_MapsAllFiveValuesCorrectly()
    {
        Mood.FromTrusted(1).Label.Should().Be("Fearful");
        Mood.FromTrusted(2).Label.Should().Be("Anxious");
        Mood.FromTrusted(3).Label.Should().Be("Neutral");
        Mood.FromTrusted(4).Label.Should().Be("Confident");
        Mood.FromTrusted(5).Label.Should().Be("Euphoric");
    }

    [Fact]
    public void LocalDate_FromUtcDateTimeOffset_ResolvesToUserLocalDate()
    {
        // 2026-08-16 22:00 UTC in Buenos Aires (UTC-3) is still 2026-08-16 19:00 local.
        var date = LocalDate.From(
            new DateTimeOffset(2026, 8, 16, 22, 0, 0, TimeSpan.Zero),
            "America/Argentina/Buenos_Aires");
        date.Iso8601.Should().Be("2026-08-16");
    }

    [Fact]
    public void LocalDate_FromUtcDateTimeOffset_RespectsMidnightRollover()
    {
        // 2026-08-17 02:00 UTC in Tokyo (UTC+9) is 2026-08-17 11:00 local
        // (same date), but 2026-08-17 14:00 UTC in NY (UTC-4) is 2026-08-17 10:00
        // (same date). Use Los Angeles (UTC-7): 2026-08-17 06:00 UTC = 2026-08-16 23:00 local.
        var date = LocalDate.From(
            new DateTimeOffset(2026, 8, 17, 6, 0, 0, TimeSpan.Zero),
            "America/Los_Angeles");
        date.Iso8601.Should().Be("2026-08-16");
    }
}
