using FluentValidation.TestHelper;
using NSubstitute.ReturnsExtensions;
using JadeCapital.Trading.Domain.Instruments;

namespace JadeCapital.Trading.UnitTests.Application.Instruments;

public class CreateInstrumentHandlerTests
{
    private readonly IInstrumentRepository _instruments = Substitute.For<IInstrumentRepository>();
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();
    private readonly IClock _clock = Substitute.For<IClock>();
    private readonly ILogger<CreateInstrumentHandler> _logger = Substitute.For<ILogger<CreateInstrumentHandler>>();

    private static readonly DateTimeOffset FixedNow =
        new(2026, 7, 1, 12, 0, 0, TimeSpan.Zero);

    private CreateInstrumentHandler CreateSut() => new(_instruments, _uow, _clock, _logger);

    private static CreateInstrumentCommand ValidCommand() => new(
        Symbol: "EUR/USD",
        AssetClasses: AssetClass.Forex | AssetClass.Binary,
        ContractSize: 100000m,
        DecimalPlaces: 5,
        PipValue: 0.0001m,
        PayoutPercent: 0.85m);

    [Fact]
    public async Task Handle_NewSymbol_PersistsAndReturnsInstrumentDto()
    {
        _clock.UtcNow.Returns(FixedNow);
        _instruments.FindBySymbolAsync("EUR/USD", Arg.Any<CancellationToken>()).ReturnsNull();
        _uow.SaveChangesAsync(Arg.Any<CancellationToken>()).Returns(Result.Success(1));

        var result = await CreateSut().Handle(ValidCommand(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Symbol.Should().Be("EUR/USD");
        result.Value.AssetClasses.Should().Be(AssetClass.Forex | AssetClass.Binary);
        result.Value.ContractSize.Should().Be(100000m);
        result.Value.DecimalPlaces.Should().Be(5);
        result.Value.IsActive.Should().BeTrue();
        result.Value.CreatedAt.Should().Be(FixedNow);

        await _instruments.Received(1).AddAsync(Arg.Any<Instrument>(), Arg.Any<CancellationToken>());
        await _uow.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_OnlyRequiredFields_AppliesDefaults()
    {
        _clock.UtcNow.Returns(FixedNow);
        _instruments.FindBySymbolAsync("EUR/USD", Arg.Any<CancellationToken>()).ReturnsNull();
        _uow.SaveChangesAsync(Arg.Any<CancellationToken>()).Returns(Result.Success(1));

        var cmd = new CreateInstrumentCommand(
            Symbol: "EUR/USD",
            AssetClasses: AssetClass.Forex,
            ContractSize: null,
            DecimalPlaces: null,
            PipValue: null,
            PayoutPercent: null);

        var result = await CreateSut().Handle(cmd, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        // Defaults del handler: ContractSize=1, DecimalPlaces=6, PipValue=0, PayoutPercent=0.85.
        result.Value.ContractSize.Should().Be(1m);
        result.Value.DecimalPlaces.Should().Be(6);
        result.Value.PipValue.Should().Be(0m);
        result.Value.PayoutPercent.Should().Be(0.85m);
    }

    [Fact]
    public async Task Handle_DuplicateSymbol_ReturnsConflict()
    {
        _clock.UtcNow.Returns(FixedNow);
        var existing = Instrument.Create(
            Guid.NewGuid(), "EUR/USD", AssetClass.Forex, 100000m, 5, 0.0001m, 0.85m, _clock).Value;
        _instruments.FindBySymbolAsync("EUR/USD", Arg.Any<CancellationToken>()).Returns(existing);

        var result = await CreateSut().Handle(ValidCommand(), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("conflict.instrument.symbol_already_exists");
        await _instruments.DidNotReceive().AddAsync(Arg.Any<Instrument>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_InvalidSymbol_ReturnsValidationFailure()
    {
        _clock.UtcNow.Returns(FixedNow);
        _instruments.FindBySymbolAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).ReturnsNull();

        var cmd = ValidCommand() with { Symbol = "ab" }; // < 3 chars

        var result = await CreateSut().Handle(cmd, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().StartWith("validation.");
        await _instruments.DidNotReceive().AddAsync(Arg.Any<Instrument>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_NoneAssetClasses_ReturnsDomainFailure()
    {
        _clock.UtcNow.Returns(FixedNow);
        _instruments.FindBySymbolAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).ReturnsNull();

        var cmd = ValidCommand() with { AssetClasses = AssetClass.None };

        var result = await CreateSut().Handle(cmd, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("validation.instrument.asset_classes_required");
    }

    [Fact]
    public async Task Handle_InvalidAssetClassesFlag_ReturnsDomainFailure()
    {
        _clock.UtcNow.Returns(FixedNow);
        _instruments.FindBySymbolAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).ReturnsNull();

        var cmd = ValidCommand() with { AssetClasses = (AssetClass)32 };

        var result = await CreateSut().Handle(cmd, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("validation.instrument.asset_classes_invalid");
    }

    [Fact]
    public async Task Handle_ZeroContractSize_ReturnsDomainFailure()
    {
        _clock.UtcNow.Returns(FixedNow);
        _instruments.FindBySymbolAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).ReturnsNull();

        var cmd = ValidCommand() with { ContractSize = 0m };

        var result = await CreateSut().Handle(cmd, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("validation.instrument.contract_size_must_be_positive");
    }

    [Fact]
    public async Task Handle_NegativePipValue_ReturnsDomainFailure()
    {
        _clock.UtcNow.Returns(FixedNow);
        _instruments.FindBySymbolAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).ReturnsNull();

        var cmd = ValidCommand() with { PipValue = -0.01m };

        var result = await CreateSut().Handle(cmd, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("validation.instrument.pip_value_must_be_non_negative");
    }

    [Fact]
    public async Task Handle_PayoutPercentOutOfRange_ReturnsDomainFailure()
    {
        _clock.UtcNow.Returns(FixedNow);
        _instruments.FindBySymbolAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).ReturnsNull();

        var cmd = ValidCommand() with { PayoutPercent = 1.5m };

        var result = await CreateSut().Handle(cmd, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("validation.instrument.payout_percent_out_of_range");
    }

    [Fact]
    public void Validator_AssetClassesNone_Fails()
    {
        var sut = new CreateInstrumentValidator();
        var cmd = ValidCommand() with { AssetClasses = AssetClass.None };

        sut.TestValidate(cmd).IsValid.Should().BeFalse();
    }

    [Fact]
    public void Validator_ContractSizeZero_Fails()
    {
        var sut = new CreateInstrumentValidator();
        var cmd = ValidCommand() with { ContractSize = 0m };

        sut.TestValidate(cmd).IsValid.Should().BeFalse();
    }

    [Fact]
    public void Validator_PayoutPercentOutOfRange_Fails()
    {
        var sut = new CreateInstrumentValidator();
        var cmd = ValidCommand() with { PayoutPercent = 1.5m };

        sut.TestValidate(cmd).IsValid.Should().BeFalse();
    }
}
