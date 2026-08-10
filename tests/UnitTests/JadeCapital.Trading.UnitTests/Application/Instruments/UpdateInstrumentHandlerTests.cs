using FluentValidation.TestHelper;
using NSubstitute.ReturnsExtensions;
using JadeCapital.Trading.Domain.Instruments;

namespace JadeCapital.Trading.UnitTests.Application.Instruments;

public class UpdateInstrumentHandlerTests
{
    private readonly IInstrumentRepository _instruments = Substitute.For<IInstrumentRepository>();
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();
    private readonly ILogger<UpdateInstrumentHandler> _logger = Substitute.For<ILogger<UpdateInstrumentHandler>>();

    private UpdateInstrumentHandler CreateSut() => new(_instruments, _uow, _logger);

    private static Instrument CreateActiveInstrument()
    {
        var c = Substitute.For<IClock>();
        c.UtcNow.Returns(new DateTimeOffset(2026, 7, 1, 10, 0, 0, TimeSpan.Zero));
        return Instrument.Create(
            Guid.NewGuid(), "EUR/USD", AssetClass.Forex,
            100000m, 5, 0.0001m, 0.85m, c).Value;
    }

    private static UpdateInstrumentCommand ValidCommand(Guid instrumentId) => new(
        InstrumentId: instrumentId,
        Symbol: "GBP/USD",
        AssetClasses: AssetClass.Forex | AssetClass.Binary,
        ContractSize: 100000m,
        DecimalPlaces: 5,
        PipValue: 0.0001m,
        PayoutPercent: 0.90m);

    [Fact]
    public async Task Handle_ExistingInstrument_UpdatesAndReturnsDto()
    {
        var instrument = CreateActiveInstrument();
        _instruments.FindByIdAsync(instrument.Id, Arg.Any<CancellationToken>()).Returns(instrument);
        _uow.SaveChangesAsync(Arg.Any<CancellationToken>()).Returns(Result.Success(1));

        var result = await CreateSut().Handle(ValidCommand(instrument.Id), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Symbol.Should().Be("GBP/USD");
        result.Value.AssetClasses.Should().Be(AssetClass.Forex | AssetClass.Binary);
        result.Value.PayoutPercent.Should().Be(0.90m);
        await _uow.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_OnlyRequiredFields_AppliesDefaults()
    {
        var instrument = CreateActiveInstrument();
        _instruments.FindByIdAsync(instrument.Id, Arg.Any<CancellationToken>()).Returns(instrument);
        _uow.SaveChangesAsync(Arg.Any<CancellationToken>()).Returns(Result.Success(1));

        var cmd = new UpdateInstrumentCommand(
            InstrumentId: instrument.Id,
            Symbol: "GBP/USD",
            AssetClasses: AssetClass.Forex,
            ContractSize: null,
            DecimalPlaces: null,
            PipValue: null,
            PayoutPercent: null);

        var result = await CreateSut().Handle(cmd, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.ContractSize.Should().Be(1m);
        result.Value.DecimalPlaces.Should().Be(6);
        result.Value.PipValue.Should().Be(0m);
        result.Value.PayoutPercent.Should().Be(0.85m);
    }

    [Fact]
    public async Task Handle_NotFound_ReturnsNotFound()
    {
        _instruments.FindByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).ReturnsNull();

        var result = await CreateSut().Handle(ValidCommand(Guid.NewGuid()), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("notfound.instrument");
    }

    [Fact]
    public async Task Handle_NoneAssetClasses_ReturnsDomainFailure()
    {
        var instrument = CreateActiveInstrument();
        _instruments.FindByIdAsync(instrument.Id, Arg.Any<CancellationToken>()).Returns(instrument);

        var cmd = ValidCommand(instrument.Id) with { AssetClasses = AssetClass.None };
        var result = await CreateSut().Handle(cmd, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("validation.instrument.asset_classes_required");
        await _uow.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_InvalidContractSize_ReturnsValidationFailure()
    {
        var instrument = CreateActiveInstrument();
        _instruments.FindByIdAsync(instrument.Id, Arg.Any<CancellationToken>()).Returns(instrument);

        var cmd = ValidCommand(instrument.Id) with { ContractSize = 0m };
        var result = await CreateSut().Handle(cmd, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().StartWith("validation.");
        await _uow.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public void Validator_NoneAssetClasses_Fails()
    {
        var sut = new UpdateInstrumentValidator();
        var cmd = ValidCommand(Guid.NewGuid()) with { AssetClasses = AssetClass.None };

        sut.TestValidate(cmd).IsValid.Should().BeFalse();
    }
}
