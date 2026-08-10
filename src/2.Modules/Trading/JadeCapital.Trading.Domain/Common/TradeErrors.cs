namespace JadeCapital.Trading.Domain.Common;

/// <summary>
/// Acceso directo (flat) a los errores mas comunes del bounded context Trading.
/// Para recorrerlos todos, usar TradingDomainErrors.Trade / .Symbol.
/// </summary>
public static class TradeErrors
{
    public static readonly Shared.Kernel.Results.Error IdRequired =
        TradingDomainErrors.Trade.IdRequired;

    public static readonly Shared.Kernel.Results.Error UserIdRequired =
        TradingDomainErrors.Trade.UserIdRequired;

    public static readonly Shared.Kernel.Results.Error AccountIdRequired =
        TradingDomainErrors.Trade.AccountIdRequired;

    public static readonly Shared.Kernel.Results.Error InstrumentIdRequired =
        TradingDomainErrors.Trade.InstrumentIdRequired;

    public static readonly Shared.Kernel.Results.Error VolumeMustBePositive =
        TradingDomainErrors.Trade.VolumeMustBePositive;

    public static readonly Shared.Kernel.Results.Error EntryPriceMustBePositive =
        TradingDomainErrors.Trade.EntryPriceMustBePositive;

    public static readonly Shared.Kernel.Results.Error ExitPriceMustBePositive =
        TradingDomainErrors.Trade.ExitPriceMustBePositive;

    public static readonly Shared.Kernel.Results.Error EntryPriceCurrencyMismatch =
        TradingDomainErrors.Trade.EntryPriceCurrencyMismatch;

    public static readonly Shared.Kernel.Results.Error ExitPriceCurrencyMismatch =
        TradingDomainErrors.Trade.ExitPriceCurrencyMismatch;

    public static readonly Shared.Kernel.Results.Error CurrencyMismatch =
        TradingDomainErrors.Trade.CurrencyMismatch;

    public static readonly Shared.Kernel.Results.Error AlreadyClosed =
        TradingDomainErrors.Trade.AlreadyClosed;

    public static readonly Shared.Kernel.Results.Error StrategyTooLong =
        TradingDomainErrors.Trade.StrategyTooLong;

    public static readonly Shared.Kernel.Results.Error NotesTooLong =
        TradingDomainErrors.Trade.NotesTooLong;

    public static readonly Shared.Kernel.Results.Error AccountCurrencyRequired =
        TradingDomainErrors.Trade.AccountCurrencyRequired;
}
