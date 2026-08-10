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
///   InstrumentId (y via projection EF, su Symbol / AssetClasses).
/// - La relacion Instrument &lt;-&gt; Account NO existe: cualquier cuenta puede
///   operar cualquier instrumento. Los payoutPercent especificos por cuenta se
///   manejan via Account.PayoutPercent (overrides a nivel del cliente).
/// - AssetClasses es un [Flags] enum: un mismo Instrument puede servir para
///   varios mercados (ej. EUR/USD = Forex | Binary).
///
/// Seed inicial: EUR/USD, GBP/USD, USD/JPY, AUD/USD (forex), XAU/USD (commodity),
/// BTC/USD, ETH/USD (crypto). Ver migracion SQL.
/// </summary>
public sealed class Instrument : Entity<Guid>
{
    private const AssetClass AllValidFlags = AssetClass.Forex | AssetClass.Crypto | AssetClass.Binary | AssetClass.Commodity | AssetClass.Other;

    public Symbol Symbol { get; private set; } = default!;
    public AssetClass AssetClasses { get; private set; }
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
        AssetClass assetClasses,
        decimal contractSize,
        int decimalPlaces,
        decimal pipValue,
        decimal payoutPercent,
        DateTimeOffset createdAt) : base(id)
    {
        Symbol = symbol;
        AssetClasses = assetClasses;
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
    /// pipValue &gt;= 0, payoutPercent en [0, 1], assetClasses != None y todos
    /// los flags dentro del rango valido (Forex|Crypto|Binary|Commodity|Other).
    /// Estado inicial: IsActive = true.
    /// </summary>
    public static Result<Instrument> Create(
        Guid id,
        string symbol,
        AssetClass assetClasses,
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

        if (symbolResult.Value.Value.Length > Symbol.MaxLength)
            return Result.Failure<Instrument>(TradingDomainErrors.Instrument.SymbolTooLong);

        if (assetClasses == AssetClass.None)
            return Result.Failure<Instrument>(TradingDomainErrors.Instrument.AssetClassesRequired);

        // Cualquier bit fuera del mask 0b11111 (Forex|Crypto|Binary|Commodity|Other) es invalido.
        if ((assetClasses & ~AllValidFlags) != 0)
            return Result.Failure<Instrument>(TradingDomainErrors.Instrument.AssetClassesInvalid);

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
            assetClasses,
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
        AssetClass assetClasses,
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
            assetClasses,
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
        AssetClass assetClasses,
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

        if (assetClasses == AssetClass.None)
            return Result.Failure(TradingDomainErrors.Instrument.AssetClassesRequired);

        if ((assetClasses & ~AllValidFlags) != 0)
            return Result.Failure(TradingDomainErrors.Instrument.AssetClassesInvalid);

        if (contractSize <= 0m)
            return Result.Failure(TradingDomainErrors.Instrument.ContractSizeMustBePositive);

        if (decimalPlaces < 0)
            return Result.Failure(TradingDomainErrors.Instrument.DecimalPlacesMustBeNonNegative);

        if (pipValue < 0m)
            return Result.Failure(TradingDomainErrors.Instrument.PipValueMustBeNonNegative);

        if (payoutPercent < 0m || payoutPercent > 1m)
            return Result.Failure(TradingDomainErrors.Instrument.PayoutPercentOutOfRange);

        Symbol = symbolResult.Value;
        AssetClasses = assetClasses;
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
