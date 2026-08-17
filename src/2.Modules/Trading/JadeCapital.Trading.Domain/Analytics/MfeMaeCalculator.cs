using JadeCapital.Trading.Domain.Trades;

namespace JadeCapital.Trading.Domain.Analytics;

// ============================================================================
//  MfeMaeCalculator — slice 2c (Trader Journal Core / MFE/MAE Charts).
//
//  Pure function: Trade -> (Mfe, Mae, Currency).
//  No persistence, no I/O, no MediatR. Same input → same output.
//
//  Wave 2 deterministic approximation (no tick data yet):
//
//    - Si el trade esta Open (ExitPrice = null o PnL = null)
//      → return (null, null, AccountCurrency). El endpoint sirve null/null
//      a un trade abierto (el spec lo manda asi).
//
//    - Si PnL.Amount > 0 (winner):
//        MFE = PnL.Amount         (≥ 0)
//        MAE = 0                  (no podemos saber el low sin tick data)
//
//    - Si PnL.Amount < 0 (loser):
//        MAE = -|PnL.Amount|      (≤ 0)
//        MFE = 0                  (no podemos saber el high sin tick data)
//
//    - Si PnL.Amount == 0 (break-even):
//        MFE = 0, MAE = 0         (cubre el caso zero P&L del spec)
//
//    - Currency = AccountCurrency del trade. El FE renderiza MFE/MAE en la
//      misma moneda que el P&L del trade (consistente con el resto de la UI).
//
//  Wave 4 reemplazara esto con datos reales de ticks / orderbook; la API
//  publica (Compute(trade) → tuple) se mantiene igual para que el FE no
//  necesite cambios.
//
//  El calculo se aplica dentro de Trade.Close() — mismo aggregate, mismo
//  SaveChanges, misma transaccion (atomicidad garantizada por el UoW, no
//  hace falta domain event dispatcher).
// ============================================================================

public static class MfeMaeCalculator
{
    /// <summary>
    /// Compute the Wave 2 MFE/MAE approximation for a single trade.
    /// </summary>
    /// <returns>
    /// Tuple of (MFE, MAE, currency). Both amounts are <c>null</c> when
    /// the trade is still open. <c>currency</c> is always the trade's
    /// AccountCurrency so the wire contract is uniform.
    /// </returns>
    public static (decimal? Mfe, decimal? Mae, string Currency) Compute(Trade trade)
    {
        ArgumentNullException.ThrowIfNull(trade);

        // Open trades have neither ExitPrice nor PnL — the approximation
        // doesn't apply. Wire contract: null/null + currency.
        if (trade.ExitPrice is null || trade.PnL is null)
        {
            return (null, null, trade.AccountCurrency);
        }

        var pnlAmount = trade.PnL.Amount;
        var currency = trade.AccountCurrency;

        // PnL > 0  → winner. MFE is the realized profit; MAE is unknown
        //            (approximation: 0).
        // PnL < 0  → loser.  MAE is the realized loss (sign-flipped to
        //            ≤ 0); MFE is unknown (approximation: 0).
        // PnL == 0 → break-even. Both fields are 0.
        if (pnlAmount > 0m)
        {
            return (pnlAmount, 0m, currency);
        }

        if (pnlAmount < 0m)
        {
            return (0m, -Math.Abs(pnlAmount), currency);
        }

        // Break-even: explicit branch so the intent is obvious to a reader.
        return (0m, 0m, currency);
    }
}