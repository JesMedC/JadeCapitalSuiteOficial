using JadeCapital.Shared.Kernel.Results;

namespace JadeCapital.Trading.Domain.Common;

/// <summary>
/// Errores semanticos del bounded context Trading. Codigos
/// "{boundedContext}.{entidad}.{detalle}" para que DomainGuard enrute a
/// la excepcion correcta segun prefijo.
/// </summary>
public static class TradingDomainErrors
{
    public static class Trade
    {
        public static readonly Error IdRequired =
            Error.Validation("trade.id_required", "Trade id is required.");

        public static readonly Error UserIdRequired =
            Error.Validation("trade.user_id_required", "User id is required.");

        public static readonly Error VolumeMustBePositive =
            Error.Validation("trade.volume_must_be_positive", "Volume must be greater than zero.");

        public static readonly Error EntryPriceMustBePositive =
            Error.Validation("trade.entry_price_must_be_positive", "Entry price must be greater than zero.");

        public static readonly Error ExitPriceMustBePositive =
            Error.Validation("trade.exit_price_must_be_positive", "Exit price must be greater than zero.");

        public static readonly Error EntryPriceCurrencyMismatch =
            Error.Validation("trade.entry_price_currency_mismatch", "Entry price currency does not match the symbol's quote currency.");

        public static readonly Error ExitPriceCurrencyMismatch =
            Error.Validation("trade.exit_price_currency_mismatch", "Exit price currency does not match the entry price currency.");

        public static readonly Error CurrencyMismatch =
            Error.Validation("trade.currency_mismatch", "Currencies must match.");

        public static readonly Error AlreadyClosed =
            Error.Conflict("trade.already_closed", "Trade is already closed or cancelled.");

        public static readonly Error AlreadyCancelled =
            Error.Conflict("trade.already_cancelled", "Trade is already cancelled and cannot be modified.");

        public static readonly Error StrategyTooLong =
            Error.Validation("trade.strategy_too_long", "Strategy must be at most 80 characters.");

        public static readonly Error NotesTooLong =
            Error.Validation("trade.notes_too_long", "Notes must be at most 2000 characters.");

        public static readonly Error AccountCurrencyRequired =
            Error.Validation("trade.account_currency_required", "Account currency code is required.");
    }

    public static class Symbol
    {
        public static readonly Error ValueRequired =
            Error.Validation("symbol.value_required", "Symbol value is required.");

        public static readonly Error ValueTooShort =
            Error.Validation("symbol.value_too_short", "Symbol must be at least 3 characters.");

        public static readonly Error ValueTooLong =
            Error.Validation("symbol.value_too_long", "Symbol must be at most 20 characters.");

        public static readonly Error ValueInvalidFormat =
            Error.Validation("symbol.value_invalid_format", "Symbol must contain only uppercase letters, digits and '/' separator.");
    }
}
