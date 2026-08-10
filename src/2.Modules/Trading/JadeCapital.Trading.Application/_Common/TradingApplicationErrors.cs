using JadeCapital.Shared.Kernel.Results;

namespace JadeCapital.Trading.Application._Common;

/// <summary>
/// Errores de aplicacion para Trading. Distintos de los errores de dominio:
/// viven en Application porque son reglas de orquestacion (ej: ownership,
/// permisos de borrado) que NO son invariantes del modelo Trade.
/// </summary>
public static class TradingApplicationErrors
{
    public static class Trades
    {
        public static readonly Error NotFound =
            Error.NotFound("trade", "Trade not found.");

        public static readonly Error Forbidden =
            Error.Forbidden("trade", "You do not have access to this trade.");

        public static readonly Error CannotDeleteClosed =
            Error.Conflict("trade.cannot_delete_closed", "Closed trades cannot be deleted.");
    }

    /// <summary>
    /// Errores de orquestacion del recurso Account: ownership y reglas
    /// referenciales que NO son invariantes del aggregate.
    /// </summary>
    public static class Accounts
    {
        public static readonly Error NotFound =
            Error.NotFound("account", "Account not found.");

        public static readonly Error Forbidden =
            Error.Forbidden("account", "You do not have access to this account.");

        public static readonly Error HasTrades =
            Error.Conflict("account.has_trades", "Cannot delete account with associated trades.");
    }

    /// <summary>
    /// Errores de orquestacion del recurso Instrument: ownership (no aplica,
    /// es global) + duplicados de symbol + reglas referenciales contra trades.
    /// </summary>
    public static class Instruments
    {
        public static readonly Error NotFound =
            Error.NotFound("instrument", "Instrument not found.");

        public static readonly Error SymbolAlreadyExists =
            Error.Conflict("instrument.symbol_already_exists", "An instrument with this symbol already exists.");

        public static readonly Error HasTrades =
            Error.Conflict("instrument.has_trades", "Cannot delete instrument with associated trades.");
    }
}