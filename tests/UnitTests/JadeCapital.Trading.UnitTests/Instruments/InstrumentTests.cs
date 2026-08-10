using JadeCapital.Trading.Domain.Instruments;

namespace JadeCapital.Trading.UnitTests.Instruments;

public class InstrumentTests
{
    private static readonly IClock Clock = Substitute.For<IClock>();

    [Fact]
    public void Create_WithValidData_CreatesActiveInstrument()
    {
        var id = Guid.NewGuid();
        var now = new DateTimeOffset(2026, 6, 1, 12, 0, 0, TimeSpan.Zero);
        Clock.UtcNow.Returns(now);

        var r = Instrument.Create(
            id, "EUR/USD", AssetClass.Forex,
            contractSize: 100000m,
            decimalPlaces: 5,
            pipValue: 0.0001m,
            payoutPercent: 0.85m,
            clock: Clock);

        r.IsSuccess.Should().BeTrue();
        var instrument = r.Value;
        instrument.Id.Should().Be(id);
        instrument.Symbol.Value.Should().Be("EUR/USD");
        instrument.AssetClasses.Should().Be(AssetClass.Forex);
        instrument.ContractSize.Should().Be(100000m);
        instrument.DecimalPlaces.Should().Be(5);
        instrument.PipValue.Should().Be(0.0001m);
        instrument.PayoutPercent.Should().Be(0.85m);
        instrument.IsActive.Should().BeTrue();
        instrument.CreatedAt.Should().Be(now);
    }

    [Fact]
    public void Create_WithMultiMarketFlags_Succeeds()
    {
        // EUR/USD puede servir para Forex Y para Binary (asset_class=5).
        var r = Instrument.Create(
            Guid.NewGuid(), "EUR/USD",
            AssetClass.Forex | AssetClass.Binary,
            100000m, 5, 0.0001m, 0.85m, Clock);

        r.IsSuccess.Should().BeTrue();
        r.Value.AssetClasses.Should().Be(AssetClass.Forex | AssetClass.Binary);
        ((int)r.Value.AssetClasses).Should().Be(5);
    }

    [Fact]
    public void Create_WithNoneAssetClasses_Fails()
    {
        var r = Instrument.Create(
            Guid.NewGuid(), "EUR/USD", AssetClass.None,
            100000m, 5, 0.0001m, 0.85m, Clock);

        r.IsFailure.Should().BeTrue();
        r.Error.Code.Should().Be("validation.instrument.asset_classes_required");
    }

    [Fact]
    public void Create_WithInvalidFlagBeyondRange_Fails()
    {
        // 32 (bit 5) no esta definido en el enum.
        var r = Instrument.Create(
            Guid.NewGuid(), "EUR/USD", (AssetClass)32,
            100000m, 5, 0.0001m, 0.85m, Clock);

        r.IsFailure.Should().BeTrue();
        r.Error.Code.Should().Be("validation.instrument.asset_classes_invalid");
    }

    [Fact]
    public void Create_WithMixOfValidAndInvalidFlags_Fails()
    {
        // Forex (1) es valido pero combinado con 32 (invalido) -> falla.
        var r = Instrument.Create(
            Guid.NewGuid(), "EUR/USD", AssetClass.Forex | (AssetClass)32,
            100000m, 5, 0.0001m, 0.85m, Clock);

        r.IsFailure.Should().BeTrue();
        r.Error.Code.Should().Be("validation.instrument.asset_classes_invalid");
    }

    [Fact]
    public void Create_WithEmptyId_Fails()
    {
        var r = Instrument.Create(
            Guid.Empty, "EUR/USD", AssetClass.Forex,
            100000m, 5, 0.0001m, 0.85m, Clock);

        r.IsFailure.Should().BeTrue();
        r.Error.Code.Should().Be("validation.instrument.id_required");
    }

    [Fact]
    public void Create_WithEmptySymbol_Fails()
    {
        var r = Instrument.Create(
            Guid.NewGuid(), "", AssetClass.Forex,
            100000m, 5, 0.0001m, 0.85m, Clock);

        r.IsFailure.Should().BeTrue();
        r.Error.Code.Should().Be("validation.instrument.symbol_required");
    }

    [Fact]
    public void Create_WithInvalidSymbolFormat_Fails()
    {
        // "$" no es letra/digito/slash; Symbol.Create falla.
        var r = Instrument.Create(
            Guid.NewGuid(), "EUR$USD", AssetClass.Forex,
            100000m, 5, 0.0001m, 0.85m, Clock);

        r.IsFailure.Should().BeTrue();
        r.Error.Code.Should().Be("validation.instrument.symbol_required");
    }

    [Fact]
    public void Create_WithZeroContractSize_Fails()
    {
        var r = Instrument.Create(
            Guid.NewGuid(), "EUR/USD", AssetClass.Forex,
            0m, 5, 0.0001m, 0.85m, Clock);

        r.IsFailure.Should().BeTrue();
        r.Error.Code.Should().Be("validation.instrument.contract_size_must_be_positive");
    }

    [Fact]
    public void Create_WithNegativeDecimalPlaces_Fails()
    {
        var r = Instrument.Create(
            Guid.NewGuid(), "EUR/USD", AssetClass.Forex,
            100000m, -1, 0.0001m, 0.85m, Clock);

        r.IsFailure.Should().BeTrue();
        r.Error.Code.Should().Be("validation.instrument.decimal_places_must_be_non_negative");
    }

    [Fact]
    public void Create_WithNegativePipValue_Fails()
    {
        var r = Instrument.Create(
            Guid.NewGuid(), "EUR/USD", AssetClass.Forex,
            100000m, 5, -0.0001m, 0.85m, Clock);

        r.IsFailure.Should().BeTrue();
        r.Error.Code.Should().Be("validation.instrument.pip_value_must_be_non_negative");
    }

    [Fact]
    public void Create_WithPayoutPercentOutOfRange_Fails()
    {
        var r = Instrument.Create(
            Guid.NewGuid(), "EUR/USD", AssetClass.Forex,
            100000m, 5, 0.0001m, 1.5m, Clock);

        r.IsFailure.Should().BeTrue();
        r.Error.Code.Should().Be("validation.instrument.payout_percent_out_of_range");
    }

    [Fact]
    public void Create_LowercaseSymbol_NormalizesToUppercase()
    {
        var r = Instrument.Create(
            Guid.NewGuid(), "eur/usd", AssetClass.Forex,
            100000m, 5, 0.0001m, 0.85m, Clock);

        r.IsSuccess.Should().BeTrue();
        r.Value.Symbol.Value.Should().Be("EUR/USD");
    }

    [Fact]
    public void UpdateMetadata_WithValidData_Updates()
    {
        var instrument = Instrument.Create(
            Guid.NewGuid(), "EUR/USD", AssetClass.Forex,
            100000m, 5, 0.0001m, 0.85m, Clock).Value;

        var r = instrument.UpdateMetadata(
            "GBP/USD", AssetClass.Forex | AssetClass.Binary,
            50000m, 4, 0.001m, 0.80m);

        r.IsSuccess.Should().BeTrue();
        instrument.Symbol.Value.Should().Be("GBP/USD");
        instrument.AssetClasses.Should().Be(AssetClass.Forex | AssetClass.Binary);
        instrument.ContractSize.Should().Be(50000m);
        instrument.DecimalPlaces.Should().Be(4);
        instrument.PipValue.Should().Be(0.001m);
        instrument.PayoutPercent.Should().Be(0.80m);
        instrument.UpdatedAt.Should().NotBeNull();
    }

    [Fact]
    public void UpdateMetadata_WithNoneAssetClasses_Fails()
    {
        var instrument = Instrument.Create(
            Guid.NewGuid(), "EUR/USD", AssetClass.Forex,
            100000m, 5, 0.0001m, 0.85m, Clock).Value;

        var r = instrument.UpdateMetadata(
            "EUR/USD", AssetClass.None,
            100000m, 5, 0.0001m, 0.85m);

        r.IsFailure.Should().BeTrue();
        r.Error.Code.Should().Be("validation.instrument.asset_classes_required");
    }

    [Fact]
    public void UpdateMetadata_WithInvalidContractSize_Fails()
    {
        var instrument = Instrument.Create(
            Guid.NewGuid(), "EUR/USD", AssetClass.Forex,
            100000m, 5, 0.0001m, 0.85m, Clock).Value;

        var r = instrument.UpdateMetadata(
            "EUR/USD", AssetClass.Forex,
            0m, 5, 0.0001m, 0.85m);

        r.IsFailure.Should().BeTrue();
        r.Error.Code.Should().Be("validation.instrument.contract_size_must_be_positive");
    }

    [Fact]
    public void Deactivate_FromActive_Deactivates()
    {
        var instrument = Instrument.Create(
            Guid.NewGuid(), "EUR/USD", AssetClass.Forex,
            100000m, 5, 0.0001m, 0.85m, Clock).Value;

        var r = instrument.Deactivate();

        r.IsSuccess.Should().BeTrue();
        instrument.IsActive.Should().BeFalse();
    }

    [Fact]
    public void Deactivate_AlreadyInactive_Fails()
    {
        var instrument = Instrument.Create(
            Guid.NewGuid(), "EUR/USD", AssetClass.Forex,
            100000m, 5, 0.0001m, 0.85m, Clock).Value;
        instrument.Deactivate();

        var r = instrument.Deactivate();

        r.IsFailure.Should().BeTrue();
        r.Error.Code.Should().Be("conflict.instrument.already_inactive");
    }

    [Fact]
    public void Reactivate_FromInactive_Reactivates()
    {
        var instrument = Instrument.Create(
            Guid.NewGuid(), "EUR/USD", AssetClass.Forex,
            100000m, 5, 0.0001m, 0.85m, Clock).Value;
        instrument.Deactivate();

        var r = instrument.Reactivate();

        r.IsSuccess.Should().BeTrue();
        instrument.IsActive.Should().BeTrue();
    }

    [Fact]
    public void Reactivate_AlreadyActive_Fails()
    {
        var instrument = Instrument.Create(
            Guid.NewGuid(), "EUR/USD", AssetClass.Forex,
            100000m, 5, 0.0001m, 0.85m, Clock).Value;

        var r = instrument.Reactivate();

        r.IsFailure.Should().BeTrue();
        r.Error.Code.Should().Be("conflict.instrument.already_active");
    }

    [Fact]
    public void FromTrusted_PreservesCreatedAt()
    {
        var createdAt = new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero);

        var instrument = Instrument.FromTrusted(
            Guid.NewGuid(), "EUR/USD", AssetClass.Forex | AssetClass.Binary,
            100000m, 5, 0.0001m, 0.85m,
            isActive: true,
            createdAt: createdAt);

        instrument.CreatedAt.Should().Be(createdAt);
        instrument.IsActive.Should().BeTrue();
        instrument.Symbol.Value.Should().Be("EUR/USD");
        instrument.AssetClasses.Should().Be(AssetClass.Forex | AssetClass.Binary);
    }
}
