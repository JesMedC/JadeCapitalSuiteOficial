using JadeCapital.Shared.Kernel.Time;
using JadeCapital.Trading.Application.Abstractions;
using JadeCapital.Trading.Application.Features.Imports.GetImportStatus;
using JadeCapital.Trading.Domain.Imports;
using FluentAssertions;
using Xunit;

namespace JadeCapital.Trading.UnitTests.Application.Imports;

/// <summary>
/// Tests for <c>GetImportStatusHandler</c>. Cross-user isolation is
/// enforced by returning <c>NotFound</c> for jobs owned by another user
/// (NOT Forbidden — to avoid existence leaks).
/// </summary>
public class GetImportStatusHandlerTests
{
    private static readonly Guid UserId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid OtherUserId = Guid.Parse("99999999-9999-9999-9999-999999999999");
    private static readonly Guid AccountId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private const string Sha256Hex = "abcdef0123456789abcdef0123456789abcdef0123456789abcdef0123456789";

    private readonly IImportJobRepository _jobs = Substitute.For<IImportJobRepository>();
    private readonly IClock _clock = Substitute.For<IClock>();

    public GetImportStatusHandlerTests()
    {
        _clock.UtcNow.Returns(new DateTimeOffset(2026, 8, 19, 14, 0, 0, TimeSpan.Zero));
    }

    private GetImportStatusHandler CreateSut() => new(_jobs);

    [Fact]
    public async Task Handle_Own_Job_Returns_200_With_Dto()
    {
        var job = ImportJob.Begin(UserId, AccountId, Shared.Kernel.Imports.ImportFormat.Csv,
            "trades.csv", 1024, Sha256Hex, _clock).Value;
        _jobs.GetByIdAsync(job.Id, Arg.Any<CancellationToken>()).Returns(job);

        var result = await CreateSut().Handle(new GetImportStatusQuery(job.Id, UserId), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Id.Should().Be(job.Id);
        result.Value.UserId.Should().Be(UserId);
        result.Value.FileName.Should().Be("trades.csv");
    }

    [Fact]
    public async Task Handle_Other_Users_Job_Returns_404_Not_Forbidden()
    {
        var job = ImportJob.Begin(OtherUserId, AccountId, Shared.Kernel.Imports.ImportFormat.Csv,
            "trades.csv", 1024, Sha256Hex, _clock).Value;
        _jobs.GetByIdAsync(job.Id, Arg.Any<CancellationToken>()).Returns(job);

        var result = await CreateSut().Handle(new GetImportStatusQuery(job.Id, UserId), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("notfound.import_job.not_found");
    }

    [Fact]
    public async Task Handle_Completed_Job_Returns_Full_Counters()
    {
        var job = ImportJob.Begin(UserId, AccountId, Shared.Kernel.Imports.ImportFormat.Csv,
            "trades.csv", 1024, Sha256Hex, _clock).Value;
        job.MarkInProgress();
        job.RecordProgress(rowsTotal: 100, rowsImported: 95, rowsSkipped: 5, rowsErrored: 0);
        job.Complete(_clock);
        _jobs.GetByIdAsync(job.Id, Arg.Any<CancellationToken>()).Returns(job);

        var result = await CreateSut().Handle(new GetImportStatusQuery(job.Id, UserId), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Status.Should().Be((byte)ImportJobStatus.Completed);
        result.Value.RowsTotal.Should().Be(100);
        result.Value.RowsImported.Should().Be(95);
        result.Value.RowsSkipped.Should().Be(5);
        result.Value.RowsErrored.Should().Be(0);
        result.Value.FinishedAt.Should().NotBeNull();
    }
}