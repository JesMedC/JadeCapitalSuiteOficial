using JadeCapital.Shared.Kernel.Imports;
using JadeCapital.Shared.Kernel.Time;
using JadeCapital.Trading.Domain.Imports;
using FluentAssertions;
using Xunit;

namespace JadeCapital.Trading.UnitTests.Imports;

/// <summary>
/// Tests for the <see cref="ImportJob"/> aggregate root invariants. Mirrors
/// the requirements declared in importers/spec.md "ImportJob aggregate"
/// section + design.md §"ImportJob".
/// </summary>
public class ImportJobTests
{
    private static readonly Guid UserId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid AccountId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private const string Sha256Hex = "abcdef0123456789abcdef0123456789abcdef0123456789abcdef0123456789";

    private static readonly IClock Clock = new StaticClock(new DateTimeOffset(2026, 8, 19, 14, 0, 0, TimeSpan.Zero));

    [Fact]
    public void Begin_With_Valid_Payload_Creates_Pending_Job()
    {
        var result = ImportJob.Begin(
            UserId, AccountId, ImportFormat.Csv,
            "trades.csv", 1024, Sha256Hex, Clock);

        result.IsSuccess.Should().BeTrue();
        result.Value.Status.Should().Be(ImportJobStatus.Pending);
        result.Value.UserId.Should().Be(UserId);
        result.Value.AccountId.Should().Be(AccountId);
        result.Value.Format.Should().Be(ImportFormat.Csv);
        result.Value.FileName.Should().Be("trades.csv");
        result.Value.FileSha256.Should().Be(Sha256Hex);
        result.Value.StartedAt.Should().Be(Clock.UtcNow);
        result.Value.FinishedAt.Should().BeNull();
    }

    [Fact]
    public void Begin_Exactly_At_10MiB_Succeeds()
    {
        const int TenMiB = 10 * 1024 * 1024;
        var result = ImportJob.Begin(UserId, AccountId, ImportFormat.Csv, "x.csv", TenMiB, Sha256Hex, Clock);

        result.IsSuccess.Should().BeTrue();
        result.Value.FileSizeBytes.Should().Be(TenMiB);
    }

    [Fact]
    public void Begin_Over_10MiB_Fails_With_File_Too_Large()
    {
        const int OverTenMiB = 10 * 1024 * 1024 + 1;
        var result = ImportJob.Begin(UserId, AccountId, ImportFormat.Csv, "x.csv", OverTenMiB, Sha256Hex, Clock);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("validation.import_job.file_too_large");
    }

    [Fact]
    public void Begin_Zero_Size_Fails()
    {
        var result = ImportJob.Begin(UserId, AccountId, ImportFormat.Csv, "x.csv", 0, Sha256Hex, Clock);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("validation.import_job.file_size_invalid");
    }

    [Fact]
    public void Begin_Empty_User_Fails()
    {
        var result = ImportJob.Begin(Guid.Empty, AccountId, ImportFormat.Csv, "x.csv", 1024, Sha256Hex, Clock);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("validation.import_job.user_id_required");
    }

    [Fact]
    public void Begin_Empty_Account_Fails()
    {
        var result = ImportJob.Begin(UserId, Guid.Empty, ImportFormat.Csv, "x.csv", 1024, Sha256Hex, Clock);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("validation.import_job.account_id_required");
    }

    [Fact]
    public void Begin_Sha256_Wrong_Length_Fails()
    {
        const string TooShort = "abc123";
        var result = ImportJob.Begin(UserId, AccountId, ImportFormat.Csv, "x.csv", 1024, TooShort, Clock);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("validation.import_job.sha256_invalid");
    }

    [Fact]
    public void Begin_Empty_FileName_Fails()
    {
        var result = ImportJob.Begin(UserId, AccountId, ImportFormat.Csv, "  ", 1024, Sha256Hex, Clock);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("validation.import_job.file_name_required");
    }

    [Fact]
    public void MarkInProgress_Transitions_From_Pending_To_InProgress()
    {
        var job = ImportJob.Begin(UserId, AccountId, ImportFormat.Csv, "x.csv", 1024, Sha256Hex, Clock).Value;

        var result = job.MarkInProgress();

        result.IsSuccess.Should().BeTrue();
        job.Status.Should().Be(ImportJobStatus.InProgress);
    }

    [Fact]
    public void RecordProgress_After_InProgress_Stores_Counters()
    {
        var job = ImportJob.Begin(UserId, AccountId, ImportFormat.Csv, "x.csv", 1024, Sha256Hex, Clock).Value;
        job.MarkInProgress();

        var result = job.RecordProgress(rowsTotal: 100, rowsImported: 50, rowsSkipped: 5, rowsErrored: 0);

        result.IsSuccess.Should().BeTrue();
        job.RowsTotal.Should().Be(100);
        job.RowsImported.Should().Be(50);
        job.RowsSkipped.Should().Be(5);
        job.RowsErrored.Should().Be(0);
    }

    [Fact]
    public void RecordProgress_Negative_Counter_Fails()
    {
        var job = ImportJob.Begin(UserId, AccountId, ImportFormat.Csv, "x.csv", 1024, Sha256Hex, Clock).Value;
        job.MarkInProgress();

        var result = job.RecordProgress(rowsTotal: 100, rowsImported: -1, rowsSkipped: 0, rowsErrored: 0);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("validation.import_job.rows_non_negative");
    }

    [Fact]
    public void Complete_From_InProgress_Sets_FinishedAt()
    {
        var job = ImportJob.Begin(UserId, AccountId, ImportFormat.Csv, "x.csv", 1024, Sha256Hex, Clock).Value;
        job.MarkInProgress();
        job.RecordProgress(100, 95, 5, 0);

        var later = new StaticClock(new DateTimeOffset(2026, 8, 19, 14, 30, 0, TimeSpan.Zero));
        var result = job.Complete(later);

        result.IsSuccess.Should().BeTrue();
        job.Status.Should().Be(ImportJobStatus.Completed);
        job.FinishedAt.Should().Be(later.UtcNow);
    }

    [Fact]
    public void Complete_From_Pending_Fails()
    {
        var job = ImportJob.Begin(UserId, AccountId, ImportFormat.Csv, "x.csv", 1024, Sha256Hex, Clock).Value;

        var result = job.Complete(Clock);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("validation.import_job.invalid_status_transition");
    }

    [Fact]
    public void Fail_From_InProgress_Preserves_Imported_Counters()
    {
        var job = ImportJob.Begin(UserId, AccountId, ImportFormat.Csv, "x.csv", 1024, Sha256Hex, Clock).Value;
        job.MarkInProgress();
        job.RecordProgress(100, 50, 0, 0);

        var result = job.Fail("DB connection lost", Clock);

        result.IsSuccess.Should().BeTrue();
        job.Status.Should().Be(ImportJobStatus.Failed);
        job.RowsImported.Should().Be(50);  // preserved
        job.ErrorMessage.Should().Be("DB connection lost");
        job.FinishedAt.Should().Be(Clock.UtcNow);
    }

    [Fact]
    public void Fail_From_Completed_Fails_With_Invalid_Transition()
    {
        var job = ImportJob.Begin(UserId, AccountId, ImportFormat.Csv, "x.csv", 1024, Sha256Hex, Clock).Value;
        job.MarkInProgress();
        job.RecordProgress(10, 10, 0, 0);
        job.Complete(Clock);

        var result = job.Fail("should not happen", Clock);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("validation.import_job.invalid_status_transition");
    }

    [Fact]
    public void Begin_Twice_With_Same_Sha256_Succeeds_But_App_Handler_Must_Dedupe()
    {
        // Aggregate doesn't enforce sha256 dedupe — that's a handler concern
        // (BeginImportHandler checks via repository BEFORE Begin).
        // The aggregate IS responsible for the field being well-formed (64 chars).
        var a = ImportJob.Begin(UserId, AccountId, ImportFormat.Csv, "a.csv", 1024, Sha256Hex, Clock);
        var b = ImportJob.Begin(UserId, AccountId, ImportFormat.Csv, "b.csv", 1024, Sha256Hex, Clock);

        a.IsSuccess.Should().BeTrue();
        b.IsSuccess.Should().BeTrue();
        a.Value.Id.Should().NotBe(b.Value.Id);  // distinct ids, even with same sha
    }

    private sealed class StaticClock : IClock
    {
        private readonly DateTimeOffset _now;
        public StaticClock(DateTimeOffset now) { _now = now; }
        public DateTimeOffset UtcNow => _now;
    }
}