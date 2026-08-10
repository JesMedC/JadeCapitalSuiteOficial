using JadeCapital.Shared.Kernel.Primitives;
using JadeCapital.Shared.Kernel.Results;
using JadeCapital.Shared.Kernel.Time;
using JadeCapital.Trading.Domain.Common;
using JadeCapital.Trading.Domain.Enums;
using JadeCapital.Trading.Domain.ValueObjects;

namespace JadeCapital.Trading.Domain.Instruments;

/// <summary>
/// Instrumento de trading. NO es Aggregate Root: es una Entity compartida
/// por todos los usuarios (seed inicial + creacion opcional desde Settings).
///
/// Las reglas de negocio que justifican este shape:
/// - El "contrato" (contract size, pip value, payout) lo define el broker / market,
///   no el usuario individual. Por eso vive una sola vez en la tabla.
/// - La relacion Instrument &lt;-&gt; Trade es FK directa: un Trade conoce su
///   InstrumentId (y via projection EF, su Symbol / AssetClass).
/// - La relacion Instrument &lt;-&gt; Account NO existe: cualquier cuenta puede
///   operar cualquier instrumento. Los payoutPercent especificos por cuenta se
///   manejan via Account.PayoutPercent (overrides a nivel del cliente).
///
/// Seed inicial: EUR/USD, GBP/USD, USD/JPY, AUD/USD (forex), XAU/USD (commodity),
/// BTC/USD, ETH/USD (crypto). Ver migracion SQL.
/// </summary>
public sealed class Instrument : Entity<Guid>
{
    public Symbol Symbol { get; private set; } = default!;
    public AssetClass AssetClass { get; private set; }
    public decimal ContractSize { get; private set; }
    public int DecimalPlaces { get; private set; }
    public decimal PipValue { get; private set; }
    public decimal PayoutPercent { get; private set; }
    public bool IsActive { get; private set; }

    // EF Core.
    private Instrument() { }

    private Instrument(
        Guid id,
        Symbol symbol,
        AssetClass assetClass,
        decimal contractSize,
        int decimalPlaces,
        decimal pipValue,
        decimal payoutPercent,
        DateTimeOffset createdAt) : base(id)
    {
        Symbol = symbol;
        AssetClass = assetClass;
        ContractSize = contractSize;
        DecimalPlaces = decimalPlaces;
        PipValue = pipValue;
        PayoutPercent = payoutPercent;
        IsActive = true;
        CreatedAt = createdAt;
    }

    /// <summary>
    /// Crea un instrumento. Validaciones: id != Guid.Empty, symbol valido (usa
    /// <see cref="Symbol.Create"/>), contractSize &gt; 0, decimalPlaces &gt;= 0,
    /// pipValue &gt;= 0, payoutPercent en [0, 1]. Estado inicial: IsActive = true.
    /// </summary>
    public static Result<Instrument> Create(
        Guid id,
        string symbol,
        AssetClass assetClass,
        decimal contractSize,
        int decimalPlaces,
        decimal pipValue,
        decimal payoutPercent,
        IClock clock)
    {
        if (id == Guid.Empty)
            return Result.Failure<Instrument>(TradingDomainErrors.Instrument.IdRequired);

        if (string.IsNullOrWhiteSpace(symbol))
            return Result.Failure<Instrument>(TradingDomainErrors.Instrument.SymbolRequired);

        var symbolResult = Symbol.Create(symbol);
        if (symbolResult.IsFailure)
            return Result.Failure<Instrument>(TradingDomainErrors.Instrument.SymbolRequired);

        // Symbol.Create normaliza a <= 20 chars (MaxLength), por lo que el check
        // explicito "TooLong" seria redundante. Lo dejamos como cobertura defensiva
        // por si en el futuro Symbol relaja su MaxLength.
        if (symbolResult.Value.Value.Length > Symbol.MaxLength)
            return Result.Failure<Instrument>(TradingDomainErrors.Instrument.SymbolTooLong);

        if (contractSize <= 0m)
            return Result.Failure<Instrument>(TradingDomainErrors.Instrument.ContractSizeMustBePositive);

        if (decimalPlaces < 0)
            return Result.Failure<Instrument>(TradingDomainErrors.Instrument.DecimalPlacesMustBeNonNegative);

        if (pipValue < 0m)
            return Result.Failure<Instrument>(TradingDomainErrors.Instrument.PipValueMustBeNonNegative);

        if (payoutPercent < 0m || payoutPercent > 1m)
            return Result.Failure<Instrument>(TradingDomainErrors.Instrument.PayoutPercentOutOfRange);

        var instrument = new Instrument(
            id,
            symbolResult.Value,
            assetClass,
            contractSize,
            decimalPlaces,
            pipValue,
            payoutPercent,
            clock.UtcNow);

        return Result.Success(instrument);
    }

    /// <summary>
    /// Acceso sin validacion para Infrastructure al hidratar desde DB.
    /// NO emite eventos, NO genera CreatedAt (lo trae del row).
    /// Usar solo en seed/migrations.
    /// </summary>
    public static Instrument FromTrusted(
        Guid id,
        string symbol,
        AssetClass assetClass,
        decimal contractSize,
        int decimalPlaces,
        decimal pipValue,
        decimal payoutPercent,
        bool isActive,
        DateTimeOffset createdAt)
    {
        var instrument = new Instrument(
            id,
            Symbol.FromTrusted(symbol),
            assetClass,
            contractSize,
            decimalPlaces,
            pipValue,
            payoutPercent,
            createdAt)
        {
            IsActive = isActive
        };
        instrument.SetCreatedAt(createdAt);
        return instrument;
    }

    /// <summary>
    /// Actualiza metadata. Mismas validaciones que Create.
    /// </summary>
    public Result UpdateMetadata(
        string symbol,
        AssetClass assetClass,
        decimal contractSize,
        int decimalPlaces,
        decimal pipValue,
        decimal payoutPercent)
    {
        if (string.IsNullOrWhiteSpace(symbol))
            return Result.Failure(TradingDomainErrors.Instrument.SymbolRequired);

        var symbolResult = Symbol.Create(symbol);
        if (symbolResult.IsFailure)
            return Result.Failure(TradingDomainErrors.Instrument.SymbolRequired);

        if (contractSize <= 0m)
            return Result.Failure(TradingDomainErrors.Instrument.ContractSizeMustBePositive);

        if (decimalPlaces < 0)
            return Result.Failure(TradingDomainErrors.Instrument.DecimalPlacesMustBeNonNegative);

        if (pipValue < 0m)
            return Result.Failure(TradingDomainErrors.Instrument.PipValueMustBeNonNegative);

        if (payoutPercent < 0m || payoutPercent > 1m)
            return Result.Failure(TradingDomainErrors.Instrument.PayoutPercentOutOfRange);

        Symbol = symbolResult.Value;
        AssetClass = assetClass;
        ContractSize = contractSize;
        DecimalPlaces = decimalPlaces;
        PipValue = pipValue;
        PayoutPercent = payoutPercent;
        Touch();

        return Result.Success();
    }

    /// <summary>Desactiva el instrumento. Falla si ya estaba inactive.</summary>
    public Result Deactivate()
    {
        if (!IsActive)
            return Result.Failure(TradingDomainErrors.Instrument.AlreadyInactive);

        IsActive = false;
        Touch();
        return Result.Success();
    }

    /// <summary>Reactiva el instrumento. Falla si ya estaba active.</summary>
    public Result Reactivate()
    {
        if (IsActive)
            return Result.Failure(TradingDomainErrors.Instrument.AlreadyActive);

        IsActive = true;
        Touch();
        return Result.Success();
    }
}
