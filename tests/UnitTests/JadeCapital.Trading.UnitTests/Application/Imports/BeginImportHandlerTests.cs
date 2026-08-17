using JadeCapital.Shared.Kernel.Imports;
using JadeCapital.Shared.Kernel.Time;
using JadeCapital.Trading.Application.Abstractions;
using JadeCapital.Trading.Application.Features.Imports.BeginImport;
using JadeCapital.Trading.Domain.Imports;
using FluentAssertions;
using Xunit;

namespace JadeCapital.Trading.UnitTests.Application.Imports;

/// <summary>
/// Tests for <c>BeginImportHandler</c>. Mirrors the "SHA-256 idempotency at
/// file level" + "Account ownership" requirements from importers/spec.md.
/// </summary>
public class BeginImportHandlerTests
{
    private static readonly Guid UserId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid AccountId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private const string Sha256Hex = "abcdef0123456789abcdef0123456789abcdef0123456789abcdef0123456789";

    private readonly IImportJobRepository _jobs = Substitute.For<IImportJobRepository>();
    private readonly IAccountRepository _accounts = Substitute.For<IAccountRepository>();
    private readonly IClock _clock = Substitute.For<IClock>();
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();

    private BeginImportHandler CreateSut() => new(_jobs, _accounts, _uow, _clock);

    private static BeginImportCommand ValidCommand() => new(
        UserId: UserId,
        AccountId: AccountId,
        FileName: "trades.csv",
        FileSizeBytes: 1024,
        FileSha256: Sha256Hex,
        DetectedFormat: ImportFormat.Csv);

    public BeginImportHandlerTests()
    {
        _clock.UtcNow.Returns(new DateTimeOffset(2026, 8, 19, 14, 0, 0, TimeSpan.Zero));
        _uow.SaveChangesAsync(Arg.Any<CancellationToken>()).Returns(Result.Success(1));
    }

    [Fact]
    public async Task Handle_Valid_Request_Creates_New_Job()
    {
        var account = JadeCapital.Trading.Domain.Accounts.Account.Open(
            AccountId, UserId, "IC Markets EUR", "IC Markets",
            JadeCapital.Trading.Domain.Enums.MarketType.Forex, "USD",
            1000m, 100m, _clock).Value;

        _jobs.FindActiveBySha256Async(UserId, Sha256Hex, Arg.Any<CancellationToken>())
            .Returns((ImportJob?)null);
        _accounts.FindByIdAsync(AccountId, Arg.Any<CancellationToken>())
            .Returns(account);

        var result = await CreateSut().Handle(ValidCommand(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.FileName.Should().Be("trades.csv");
        result.Value.FileSha256.Should().Be(Sha256Hex);
        result.Value.Status.Should().Be((byte)ImportJobStatus.Pending);
        await _jobs.Received(1).AddAsync(Arg.Any<ImportJob>(), Arg.Any<CancellationToken>());
        await _uow.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_FileSize_Over_10MiB_Fails_With_422()
    {
        var cmd = ValidCommand() with { FileSizeBytes = 11_000_000 };

        var result = await CreateSut().Handle(cmd, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("validation.import_job.file_too_large");
        await _jobs.DidNotReceive().AddAsync(Arg.Any<ImportJob>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_Empty_File_Fails()
    {
        var cmd = ValidCommand() with { FileSizeBytes = 0 };

        var result = await CreateSut().Handle(cmd, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("validation.import_job.file_size_invalid");
    }

    [Fact]
    public async Task Handle_Account_Not_Found_Returns_404()
    {
        _jobs.FindActiveBySha256Async(UserId, Sha256Hex, Arg.Any<CancellationToken>())
            .Returns((ImportJob?)null);
        _accounts.FindByIdAsync(AccountId, Arg.Any<CancellationToken>())
            .Returns((JadeCapital.Trading.Domain.Accounts.Account?)null);

        var result = await CreateSut().Handle(ValidCommand(), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("notfound.import_job.account_not_found");
        await _jobs.DidNotReceive().AddAsync(Arg.Any<ImportJob>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_Account_Owned_By_Other_User_Returns_404()
    {
        var otherUser = Guid.Parse("99999999-9999-9999-9999-999999999999");
        var account = JadeCapital.Trading.Domain.Accounts.Account.Open(
            AccountId, otherUser, "Other", "Broker",
            JadeCapital.Trading.Domain.Enums.MarketType.Forex, "USD",
            1000m, 100m, _clock).Value;

        _jobs.FindActiveBySha256Async(UserId, Sha256Hex, Arg.Any<CancellationToken>())
            .Returns((ImportJob?)null);
        _accounts.FindByIdAsync(AccountId, Arg.Any<CancellationToken>()).Returns(account);

        var result = await CreateSut().Handle(ValidCommand(), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("notfound.import_job.account_not_found");
    }

    [Fact]
    public async Task Handle_Sha256_Collision_Returns_409_With_Existing_JobId()
    {
        var existingId = Guid.NewGuid();
        var existingJob = ImportJob.Begin(UserId, AccountId, ImportFormat.Csv, "trades.csv",
            1024, Sha256Hex, _clock).Value;
        // Mutate the id via reflection-style substitution (it's read-only after Begin).
        // Easier: just verify the error code is 409 + has existingJobId via mapping.
        _jobs.FindActiveBySha256Async(UserId, Sha256Hex, Arg.Any<CancellationToken>())
            .Returns(existingJob);

        var result = await CreateSut().Handle(ValidCommand(), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("conflict.import_job.duplicate");
        result.Error.Message.Should().Contain(existingJob.Id.ToString());
        await _jobs.DidNotReceive().AddAsync(Arg.Any<ImportJob>(), Arg.Any<CancellationToken>());
    }
}