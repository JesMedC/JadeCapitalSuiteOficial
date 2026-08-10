using JadeCapital.Shared.Kernel.Money;
using JadeCapital.Shared.Kernel.Primitives;
using JadeCapital.Shared.Kernel.Results;
using JadeCapital.Shared.Kernel.Time;
using JadeCapital.Trading.Domain.Common;
using JadeCapital.Trading.Domain.Enums;
using JadeCapital.Trading.Domain.ValueObjects;

namespace JadeCapital.Trading.Domain.Trades;

/// <summary>
/// Aggregate Root de un trade.
//
// Representa una operacion individual (apertura -> [cierre|cancelacion]).
// Reglas de negocio:
// - Un trade pertenece a un unico user (UserId FK; NO navigation property en domain).
// - Volume, EntryPrice y ExitPrice (cuando aplica) estan en la MISMA moneda:
//   la quotable del simbolo (USD para EUR/USD, USDT para BTC/USDT, etc).
//   Esto evita conversion FX en el dominio: la conversion es responsabilidad
//   de Application/Infrastructure que conoce la cuenta del usuario.
// - PnL se calcula solo en Close: (exit - entry) * volume para Long, inverso para Short.
// - Strategy/Notes son metadata opcional acotada en longitud (80 / 2000).
// - Estado terminal: Close o Cancel. No se permite re-abrir.
///
/// Transiciones validas: Open -> Closed | Cancelled.
/// </summary>
public sealed class Trade : AggregateRoot<Guid>
{
    public const int MaxStrategyLength = 80;
    public const int MaxNotesLength = 2000;

    public Guid UserId { get; private set; }
    public Symbol Symbol { get; private set; } = default!;
    public AssetClass AssetClass { get; private set; }
    public TradeDirection Direction { get; private set; }
    public TradeStatus Status { get; private set; }

    public Money Volume { get; private set; } = default!;
    public Money EntryPrice { get; private set; } = default!;
    public Money? ExitPrice { get; private set; }
    public Money? PnL { get; private set; }

    public string? Strategy { get; private set; }
    public string? Notes { get; private set; }

    public DateTimeOffset OpenedAt { get; private set; }
    public DateTimeOffset? ClosedAt { get; private set; }

    /// <summary>
    /// Codigo de la cuenta del usuario (3 letras ISO 4217-like). Se persiste como
    /// string para no acoplar el aggregate a la clase Currency del Shared.Kernel
    /// en queries EF. Se valida formato en Open.
    /// </summary>
    public string AccountCurrency { get; private set; } = default!;

    // EF Core.
    private Trade() { }

    private Trade(
        Guid id,
        Guid userId,
        Symbol symbol,
        AssetClass assetClass,
        TradeDirection direction,
        Money volume,
        Money entryPrice,
        string accountCurrency,
        string? strategy,
        string? notes,
        DateTimeOffset openedAt) : base(id)
    {
        UserId = userId;
        Symbol = symbol;
        AssetClass = assetClass;
        Direction = direction;
        Status = TradeStatus.Open;
        Volume = volume;
        EntryPrice = entryPrice;
        AccountCurrency = accountCurrency;
        Strategy = strategy;
        Notes = notes;
        OpenedAt = openedAt;
    }

    /// <summary>
    /// Abre un nuevo trade. Validaciones:
    /// - id y userId != Guid.Empty
    /// - Volume.Amount &gt; 0, EntryPrice.Amount &gt; 0
    /// - EntryPrice.Currency == Symbol.InferQuoteCurrencyCode() (sanity check)
    /// - accountCurrency es 3 letras mayusculas
    /// - Strategy/Notes null o dentro del max length
    /// </summary>
    public static Result<Trade> Open(
        Guid id,
        Guid userId,
        Symbol symbol,
        AssetClass assetClass,
        TradeDirection direction,
        Money volume,
        Money entryPrice,
        string accountCurrency,
        string? strategy,
        string? notes,
        DateTimeOffset openedAt)
    {
        if (id == Guid.Empty)
            return Result.Failure<Trade>(TradeErrors.IdRequired);

        if (userId == Guid.Empty)
            return Result.Failure<Trade>(TradeErrors.UserIdRequired);

        if (volume is null || volume.Amount <= 0m)
            return Result.Failure<Trade>(TradeErrors.VolumeMustBePositive);

        if (entryPrice is null || entryPrice.Amount <= 0m)
            return Result.Failure<Trade>(TradeErrors.EntryPriceMustBePositive);

        if (string.IsNullOrWhiteSpace(accountCurrency) || accountCurrency.Trim().Length != 3)
            return Result.Failure<Trade>(TradeErrors.AccountCurrencyRequired);

        var normalizedAccountCurrency = accountCurrency.Trim().ToUpperInvariant();

        var quoteCurrencyCode = symbol.InferQuoteCurrencyCode();
        if (!string.IsNullOrEmpty(quoteCurrencyCode)
            && !string.Equals(entryPrice.Currency.Code, quoteCurrencyCode, StringComparison.Ordinal))
        {
            return Result.Failure<Trade>(TradeErrors.EntryPriceCurrencyMismatch);
        }

        if (strategy is not null && strategy.Length > MaxStrategyLength)
            return Result.Failure<Trade>(TradeErrors.StrategyTooLong);

        if (notes is not null && notes.Length > MaxNotesLength)
            return Result.Failure<Trade>(TradeErrors.NotesTooLong);

        var trade = new Trade(
            id, userId, symbol, assetClass, direction,
            volume, entryPrice, normalizedAccountCurrency,
            strategy, notes, openedAt);

        trade.RaiseDomainEvent(new TradeOpenedDomainEvent(
            trade.Id, trade.UserId, trade.Symbol.Value, trade.AssetClass,
            trade.Direction, trade.Volume.Amount, trade.EntryPrice.Amount,
            trade.AccountCurrency, trade.OpenedAt));

        return Result.Success(trade);
    }

    /// <summary>
    /// Cierra un trade abierto. Calcula PnL en account currency:
    /// - Long:  (exit - entry) * volume
    /// - Short: (entry - exit) * volume
    /// Falla si el trade ya no esta Open, si ExitPrice no es positivo, o si
    /// ExitPrice.Currency difiere de EntryPrice.Currency.
    /// </summary>
    public Result Close(Money exitPrice, DateTimeOffset closedAt, IClock clock)
    {
        if (Status != TradeStatus.Open)
            return Result.Failure(TradeErrors.AlreadyClosed);

        if (exitPrice is null || exitPrice.Amount <= 0m)
            return Result.Failure(TradeErrors.ExitPriceMustBePositive);

        if (!exitPrice.Currency.Equals(EntryPrice.Currency))
            return Result.Failure(TradeErrors.ExitPriceCurrencyMismatch);

        // diff = exit - entry (Long)  |  entry - exit (Short). Misma moneda.
        var diffResult = Direction == TradeDirection.Long
            ? exitPrice - EntryPrice
            : EntryPrice - exitPrice;

        if (diffResult.IsFailure)
            return Result.Failure(diffResult.Error);

        // PnL = diff * volume.Amount (account currency = volume.Currency).
        var pnlResult = diffResult.Value * Volume.Amount;
        if (pnlResult.IsFailure)
            return Result.Failure(pnlResult.Error);

        ExitPrice = exitPrice;
        PnL = pnlResult.Value;
        Status = TradeStatus.Closed;
        ClosedAt = closedAt;
        Touch();

        RaiseDomainEvent(new TradeClosedDomainEvent(
            Id, UserId, PnL.Amount, AccountCurrency, ClosedAt.Value, clock.UtcNow));

        return Result.Success();
    }

    /// <summary>
    /// Cancela un trade abierto. No calcula PnL (la operacion nunca se efectuo).
    /// </summary>
    public Result Cancel(IClock clock)
    {
        if (Status != TradeStatus.Open)
            return Result.Failure(TradeErrors.AlreadyClosed);

        Status = TradeStatus.Cancelled;
        ClosedAt = clock.UtcNow;
        Touch();

        RaiseDomainEvent(new TradeCancelledDomainEvent(
            Id, UserId, ClosedAt.Value, clock.UtcNow));

        return Result.Success();
    }

    /// <summary>
    /// Actualiza metadata opcional (strategy/notes). Trimea espacios.
    /// Falla si el trade esta Cancelled (estado terminal inmutable).
    /// Closed sigue siendo modificable para correcciones post-cierre
    /// (e.g. anadir notas finales al reporte).
    /// </summary>
    public Result UpdateMetadata(string? strategy, string? notes)
    {
        if (Status == TradeStatus.Cancelled)
            return Result.Failure(TradingDomainErrors.Trade.AlreadyCancelled);

        if (strategy is not null && strategy.Length > MaxStrategyLength)
            return Result.Failure(TradeErrors.StrategyTooLong);

        if (notes is not null && notes.Length > MaxNotesLength)
            return Result.Failure(TradeErrors.NotesTooLong);

        Strategy = string.IsNullOrWhiteSpace(strategy) ? null : strategy.Trim();
        Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();
        Touch();

        return Result.Success();
    }
}
