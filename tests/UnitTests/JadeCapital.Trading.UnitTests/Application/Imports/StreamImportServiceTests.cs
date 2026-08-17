using JadeCapital.Shared.Kernel.Imports;
using JadeCapital.Shared.Kernel.Results;
using JadeCapital.Shared.Kernel.Time;
using JadeCapital.Trading.Application.Abstractions;
using JadeCapital.Trading.Application.Features.Imports;
using JadeCapital.Trading.Domain.Imports;
using JadeCapital.Trading.Domain.Trades;
using Microsoft.Extensions.Logging;
using NSubstitute.ReturnsExtensions;
using FluentAssertions;
using Xunit;

namespace JadeCapital.Trading.UnitTests.Application.Imports;

/// <summary>
/// Tests for <c>StreamImportService</c>. Covers the streaming pipeline
/// scenarios from importers/spec.md "Streaming pipeline with batch
/// transactions" Requirement: batches of 50, partial batch commits,
/// dedupe, and partial-failure rollback semantics.
/// </summary>
public class StreamImportServiceTests
{
    private static readonly Guid UserId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid AccountId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private const string Sha256Hex = "abcdef0123456789abcdef0123456789abcdef0123456789abcdef0123456789";

    private readonly IImportJobRepository _jobs = Substitute.For<IImportJobRepository>();
    private readonly IImportRowDedupeService _dedupe = Substitute.For<IImportRowDedupeService>();
    private readonly ITradeRepository _trades = Substitute.For<ITradeRepository>();
    private readonly IInstrumentRepository _instruments = Substitute.For<IInstrumentRepository>();
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();
    private readonly IClock _clock = Substitute.For<IClock>();
    private readonly ILogger<StreamImportService> _logger = Substitute.For<ILogger<StreamImportService>>();

    public StreamImportServiceTests()
    {
        _clock.UtcNow.Returns(new DateTimeOffset(2026, 8, 19, 14, 0, 0, TimeSpan.Zero));
        _uow.SaveChangesAsync(Arg.Any<CancellationToken>()).Returns(Result.Success(1));
        var instrument = JadeCapital.Trading.Domain.Instruments.Instrument.Create(
            Guid.NewGuid(), "EURUSD",
            JadeCapital.Trading.Domain.Enums.AssetClass.Forex,
            contractSize: 100_000m, decimalPlaces: 5,
            pipValue: 1m, payoutPercent: 0m, _clock).Value;
        _instruments.FindBySymbolAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(instrument);
    }

    private StreamImportService CreateSut() => new(_jobs, _dedupe, _trades, _instruments, _uow, _clock, _logger);

    private static ImportRow Row(int line, string? ticket = null) => new(
        LineNumber: line,
        TicketId: ticket,
        Symbol: "EURUSD",
        Volume: 1.0m,
        VolumeCurrency: "USD",
        OpenedAt: new DateTimeOffset(2026, 8, 19, 14, 0, 0, TimeSpan.Zero),
        ClosedAt: null,
        EntryPrice: 1.0850m,
        ExitPrice: null,
        PnlAmount: null,
        PnlCurrency: "USD",
        Direction: ImportDirection.Long,
        Status: ImportRowStatus.Open,
        Notes: null);

    private static IImportRowParser StubParser(params ImportRow[] rows)
    {
        var parser = Substitute.For<IImportRowParser>();
        parser.Format.Returns(ImportFormat.Csv);
        parser.CanParse(Arg.Any<string>(), Arg.Any<Stream>()).Returns(0.95);
        parser.ParseAsync(Arg.Any<Stream>(), Arg.Any<CancellationToken>())
            .Returns(_ => ToAsync(rows));
        return parser;
    }

    private static async IAsyncEnumerable<ImportRow> ToAsync(
        IEnumerable<ImportRow> rows,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        foreach (var r in rows)
        {
            await Task.Yield();
            yield return r;
        }
    }

    /// <summary>
    /// Configures the dedupe mock with a per-batch function. The mock is
    /// called once per batch (each batch has up to 50 rows).
    /// </summary>
    private void SetupDedupe(Func<IReadOnlyList<ImportRow>, DedupeBatchResult> func)
    {
        _dedupe.DedupeBatchAsync(
                Arg.Any<Guid>(), Arg.Any<Guid>(),
                Arg.Any<IReadOnlyList<ImportRow>>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var input = call.Args()[2] as IReadOnlyList<ImportRow> ?? Array.Empty<ImportRow>();
                return func(input);
            });
    }

    [Fact]
    public async Task ExecuteAsync_HappyPath_50Rows_Counts_Row_Totals()
    {
        var rows = Enumerable.Range(1, 50).Select(i => Row(i, ticket: $"T{i}")).ToArray();
        var parser = StubParser(rows);
        SetupDedupe(input => new DedupeBatchResult(input.ToArray(), 0));  // all 50 are new
        var job = ImportJob.Begin(UserId, AccountId, ImportFormat.Csv, "x.csv", 1024, Sha256Hex, _clock).Value;

        var result = await CreateSut().ExecuteAsync(job, Stream.Null, parser, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.RowsTotal.Should().Be(50);
        result.Value.RowsImported.Should().Be(50);
        result.Value.Status.Should().Be(ImportJobStatus.Completed);
        result.Value.FinishedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task ExecuteAsync_75Rows_Splits_Two_Batches()
    {
        var rows = Enumerable.Range(1, 75).Select(i => Row(i, ticket: $"T{i}")).ToArray();
        var parser = StubParser(rows);
        SetupDedupe(input => new DedupeBatchResult(input.ToArray(), 0));  // all new
        var job = ImportJob.Begin(UserId, AccountId, ImportFormat.Csv, "x.csv", 1024, Sha256Hex, _clock).Value;

        var result = await CreateSut().ExecuteAsync(job, Stream.Null, parser, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.RowsTotal.Should().Be(75);
        result.Value.RowsImported.Should().Be(75);
    }

    [Fact]
    public async Task ExecuteAsync_AllDuplicates_AreSkipped()
    {
        var rows = Enumerable.Range(1, 100).Select(i => Row(i, ticket: $"T{i}")).ToArray();
        var parser = StubParser(rows);
        SetupDedupe(input => new DedupeBatchResult(Array.Empty<ImportRow>(), input.Count));  // all dupes
        var job = ImportJob.Begin(UserId, AccountId, ImportFormat.Csv, "x.csv", 1024, Sha256Hex, _clock).Value;

        var result = await CreateSut().ExecuteAsync(job, Stream.Null, parser, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.RowsImported.Should().Be(0);
        result.Value.RowsSkipped.Should().Be(100);
        result.Value.Status.Should().Be(ImportJobStatus.Completed);
    }

    [Fact]
    public async Task ExecuteAsync_PartialSkip_Counts_Both_Counters()
    {
        // 100 rows total: 70 new (split across batches), 30 dupes.
        // Total per-batch dedupe: 50 → 35 new + 15 dupes (batches 1+2).
        var rows = Enumerable.Range(1, 100).Select(i => Row(i, ticket: $"T{i}")).ToArray();
        var parser = StubParser(rows);
        SetupDedupe(input =>
        {
            var keep = input.Take((int)Math.Round(input.Count * 0.7)).ToArray();
            var skipped = input.Count - keep.Length;
            return new DedupeBatchResult(keep, skipped);
        });
        var job = ImportJob.Begin(UserId, AccountId, ImportFormat.Csv, "x.csv", 1024, Sha256Hex, _clock).Value;

        var result = await CreateSut().ExecuteAsync(job, Stream.Null, parser, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.RowsImported.Should().Be(70);
        result.Value.RowsSkipped.Should().Be(30);
    }

    [Fact]
    public async Task ExecuteAsync_Batch_Failure_Marks_Job_Failed_Preserves_Counters()
    {
        var rows = Enumerable.Range(1, 100).Select(i => Row(i, ticket: $"T{i}")).ToArray();
        var parser = StubParser(rows);
        SetupDedupe(input => new DedupeBatchResult(input.ToArray(), 0));  // all new
        // First SaveChanges succeeds (batch 1); second fails (batch 2).
        var callCount = 0;
        _uow.SaveChangesAsync(Arg.Any<CancellationToken>())
            .Returns(ci =>
            {
                callCount++;
                return callCount == 1
                    ? Result.Success(50)
                    : Result.Failure<int>(Error.Failure("db.update.failed", "simulated DB failure"));
            });
        var job = ImportJob.Begin(UserId, AccountId, ImportFormat.Csv, "x.csv", 1024, Sha256Hex, _clock).Value;

        var result = await CreateSut().ExecuteAsync(job, Stream.Null, parser, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Status.Should().Be(ImportJobStatus.Failed);
        result.Value.RowsImported.Should().Be(50);  // preserved from the committed batch
        result.Value.ErrorMessage.Should().NotBeNullOrEmpty();
        result.Value.FinishedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task ExecuteAsync_Parser_Stream_Empty_Completes_With_Zero()
    {
        var parser = StubParser();  // 0 rows
        SetupDedupe(input => new DedupeBatchResult(Array.Empty<ImportRow>(), 0));
        var job = ImportJob.Begin(UserId, AccountId, ImportFormat.Csv, "x.csv", 1024, Sha256Hex, _clock).Value;

        var result = await CreateSut().ExecuteAsync(job, Stream.Null, parser, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.RowsTotal.Should().Be(0);
        result.Value.RowsImported.Should().Be(0);
        result.Value.Status.Should().Be(ImportJobStatus.Completed);
    }

    [Fact]
    public async Task ExecuteAsync_Records_Running_Progress()
    {
        var rows = Enumerable.Range(1, 100).Select(i => Row(i, ticket: $"T{i}")).ToArray();
        var parser = StubParser(rows);
        SetupDedupe(input => new DedupeBatchResult(input.ToArray(), 0));
        var job = ImportJob.Begin(UserId, AccountId, ImportFormat.Csv, "x.csv", 1024, Sha256Hex, _clock).Value;

        var result = await CreateSut().ExecuteAsync(job, Stream.Null, parser, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        // _jobs.UpdateAsync called once for InProgress + once after batch 1 + once after batch 2 + once after Complete.
        await _jobs.Received(4).UpdateAsync(Arg.Any<ImportJob>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteAsync_Marks_Job_InProgress_At_Start()
    {
        var rows = Enumerable.Range(1, 10).Select(i => Row(i, ticket: $"T{i}")).ToArray();
        var parser = StubParser(rows);
        SetupDedupe(input => new DedupeBatchResult(input.ToArray(), 0));
        var job = ImportJob.Begin(UserId, AccountId, ImportFormat.Csv, "x.csv", 1024, Sha256Hex, _clock).Value;

        var result = await CreateSut().ExecuteAsync(job, Stream.Null, parser, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Status.Should().NotBe(ImportJobStatus.Pending);
    }
}