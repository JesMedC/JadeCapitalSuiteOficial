using NSubstitute.ReturnsExtensions;
using JadeCapital.Trading.Domain.Accounts;

namespace JadeCapital.Trading.UnitTests.Application.Accounts;

public class OpenAccountHandlerTests
{
    private readonly IAccountRepository _accounts = Substitute.For<IAccountRepository>();
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();
    private readonly IClock _clock = Substitute.For<IClock>();
    private readonly ILogger<OpenAccountHandler> _logger = Substitute.For<ILogger<OpenAccountHandler>>();

    private static readonly DateTimeOffset FixedNow =
        new(2026, 7, 1, 12, 0, 0, TimeSpan.Zero);

    private OpenAccountHandler CreateSut() => new(_accounts, _uow, _clock, _logger);

    private static OpenAccountCommand ValidCommand() => new(
        UserId: Guid.NewGuid(),
        Name: "IC Markets EUR",
        Broker: "IC Markets",
        Currency: "USD",
        InitialBalance: 1000m,
        Leverage: 100m,
        PayoutPercent: 0.85m);

    [Fact]
    public async Task Handle_ValidCommand_PersistsAndReturnsAccountDto()
    {
        _clock.UtcNow.Returns(FixedNow);
        _uow.SaveChangesAsync(Arg.Any<CancellationToken>()).Returns(Result.Success(1));

        var cmd = ValidCommand();
        var result = await CreateSut().Handle(cmd, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.UserId.Should().Be(cmd.UserId);
        result.Value.Name.Should().Be("IC Markets EUR");
        result.Value.Broker.Should().Be("IC Markets");
        result.Value.Currency.Should().Be("USD");
        result.Value.InitialBalance.Should().Be(1000m);
        result.Value.Leverage.Should().Be(100m);
        result.Value.PayoutPercent.Should().Be(0.85m);
        result.Value.IsActive.Should().BeTrue();
        result.Value.CreatedAt.Should().Be(FixedNow);

        await _accounts.Received(1).AddAsync(Arg.Any<Account>(), Arg.Any<CancellationToken>());
        await _uow.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_LowercaseCurrency_NormalizesToUppercase()
    {
        _clock.UtcNow.Returns(FixedNow);
        _uow.SaveChangesAsync(Arg.Any<CancellationToken>()).Returns(Result.Success(1));

        var cmd = ValidCommand() with { Currency = "usd" };

        var result = await CreateSut().Handle(cmd, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Currency.Should().Be("USD");
    }

    [Fact]
    public async Task Handle_InvalidCurrency_ReturnsValidationFailure()
    {
        _clock.UtcNow.Returns(FixedNow);

        var cmd = ValidCommand() with { Currency = "ARS" }; // no esta en whitelist

        var result = await CreateSut().Handle(cmd, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().StartWith("validation.");
        await _accounts.DidNotReceive().AddAsync(Arg.Any<Account>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ZeroLeverage_ReturnsDomainFailure()
    {
        _clock.UtcNow.Returns(FixedNow);

        var cmd = ValidCommand() with { Leverage = 0m };

        var result = await CreateSut().Handle(cmd, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("validation.account.leverage_must_be_positive");
    }

    [Fact]
    public async Task Handle_PayoutOutOfRange_ReturnsDomainFailure()
    {
        _clock.UtcNow.Returns(FixedNow);

        var cmd = ValidCommand() with { PayoutPercent = 1.5m };

        var result = await CreateSut().Handle(cmd, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("validation.account.payout_percent_out_of_range");
    }

    [Fact]
    public async Task Handle_SaveChangesFails_ThrowsConflictDomainException()
    {
        _clock.UtcNow.Returns(FixedNow);
        _uow.SaveChangesAsync(Arg.Any<CancellationToken>())
            .Returns(Result.Failure<int>(Error.Conflict("db.concurrency", "Concurrency conflict.")));

        var act = () => CreateSut().Handle(ValidCommand(), CancellationToken.None);

        await act.Should().ThrowAsync<ConflictDomainException>()
            .WithMessage("Concurrency conflict.");
    }
}
