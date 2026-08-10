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
}