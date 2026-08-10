using JadeCapital.Shared.Kernel.Results;
using JadeCapital.Trading.Domain.Enums;

namespace JadeCapital.Trading.Domain.Common;

/// <summary>
/// Errores semanticos del bounded context Trading. Codigos
/// "{boundedContext}.{entidad}.{detalle}" para que DomainGuard enrute a
/// la excepcion correcta segun prefijo.
///
/// Nota sobre codigos: las factories de <see cref="Error"/> ya prefijan con
/// "validation." / "conflict." / etc. Por eso pasamos el codigo SIN el prefijo
/// (ej. "account.id_required") y el codigo final queda
/// "validation.account.id_required".
/// </summary>
public static class TradingDomainErrors
{
    public static class Trade
    {
        public static readonly Error IdRequired =
            Error.Validation("trade.id_required", "Trade id is required.");

        public static readonly Error UserIdRequired =
            Error.Validation("trade.user_id_required", "User id is required.");

        public static readonly Error AccountIdRequired =
            Error.Validation("trade.account_id_required", "Account id is required.");

        public static readonly Error InstrumentIdRequired =
            Error.Validation("trade.instrument_id_required", "Instrument id is required.");

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

    public static class Account
    {
        public static readonly Error IdRequired =
            Error.Validation("account.id_required", "Account id is required.");

        public static readonly Error UserIdRequired =
            Error.Validation("account.user_id_required", "User id is required.");

        public static readonly Error NameRequired =
            Error.Validation("account.name_required", "Account name is required.");

        public static readonly Error NameTooLong =
            Error.Validation("account.name_too_long", "Account name must be 80 chars or less.");

        public static readonly Error BrokerRequired =
            Error.Validation("account.broker_required", "Broker is required.");

        public static readonly Error BrokerTooLong =
            Error.Validation("account.broker_too_long", "Broker must be 80 chars or less.");

        public static readonly Error CurrencyCodeInvalid =
            Error.Validation("account.currency_code_invalid", "Account currency must be a valid 3-letter uppercase ISO 4217-like code.");

        public static readonly Error InitialBalanceMustBeNonNegative =
            Error.Validation("account.initial_balance_must_be_non_negative", "Initial balance must be zero or greater.");

        public static readonly Error LeverageMustBePositive =
            Error.Validation("account.leverage_must_be_positive", "Leverage must be greater than zero.");

        public static readonly Error LeverageRequiredForForex =
            Error.Validation("account.leverage_required_for_forex", "Leverage is required for Forex accounts.");

        public static readonly Error MarketTypeRequired =
            Error.Validation("account.market_type_required", "Market type is required.");

        public static readonly Error InvalidMarketType =
            Error.Validation("account.invalid_market_type", "Invalid market type.");

        public static readonly Error AlreadyInactive =
            Error.Conflict("account.already_inactive", "Account is already inactive.");

        public static readonly Error AlreadyActive =
            Error.Conflict("account.already_active", "Account is already active.");
    }

    public static class Instrument
    {
        public static readonly Error IdRequired =
            Error.Validation("instrument.id_required", "Instrument id is required.");

        public static readonly Error SymbolRequired =
            Error.Validation("instrument.symbol_required", "Symbol is required.");

        public static readonly Error SymbolTooLong =
            Error.Validation("instrument.symbol_too_long", "Symbol must be 20 chars or less.");

        public static readonly Error ContractSizeMustBePositive =
            Error.Validation("instrument.contract_size_must_be_positive", "Contract size must be greater than zero.");

        public static readonly Error DecimalPlacesMustBeNonNegative =
            Error.Validation("instrument.decimal_places_must_be_non_negative", "Decimal places must be zero or greater.");

        public static readonly Error PipValueMustBeNonNegative =
            Error.Validation("instrument.pip_value_must_be_non_negative", "Pip value must be zero or greater.");

        public static readonly Error PayoutPercentOutOfRange =
            Error.Validation("instrument.payout_percent_out_of_range", "Payout percent must be between 0 and 1.");

        public static readonly Error AssetClassesRequired =
            Error.Validation("instrument.asset_classes_required", "At least one asset class must be selected.");

        public static readonly Error AssetClassesInvalid =
            Error.Validation("instrument.asset_classes_invalid", "Asset classes contains an invalid flag.");

        public static readonly Error AlreadyInactive =
            Error.Conflict("instrument.already_inactive", "Instrument is already inactive.");

        public static readonly Error AlreadyActive =
            Error.Conflict("instrument.already_active", "Instrument is already active.");
    }
}
