namespace JadeCapital.Shared.Kernel.Imports;

/// <summary>
/// One trade row emitted by an <see cref="IImportRowParser"/>. The shape is
/// cross-module stable (Trading owns the streaming pipeline but Billing or
/// Identity could later consume the same record). Mirrors the
/// <see cref="JadeCapital.Trading.Domain.Trades.Trade"/> aggregate's money
/// convention (decimal + ISO-4217-like currency code, no float/double).
///
/// <para>
/// Money that doesn't apply for this row (e.g. <c>ExitPrice</c> on an open
/// trade) is <c>null</c>, never 0 — this matches the trade domain's null-PnL
/// invariant for open trades.
/// </para>
/// </summary>
/// <param name="LineNumber">1-based source line (header = 1, first data row = 2).</param>
/// <param name="TicketId">Broker-assigned id (MT4/MT5) or null when the export omits it.</param>
/// <param name="Symbol">Instrument symbol, uppercased (e.g. "EURUSD", "EUR/USD").</param>
/// <param name="Volume">Position size (lots/contracts/coins per currency).</param>
/// <param name="VolumeCurrency">ISO-4217-like currency code for the volume (e.g. "USD").</param>
/// <param name="OpenedAt">Trade open timestamp, normalized to UTC.</param>
/// <param name="ClosedAt">Trade close timestamp; null for open trades.</param>
/// <param name="EntryPrice">Quote currency price at open.</param>
/// <param name="ExitPrice">Quote currency price at close; null for open trades.</param>
/// <param name="PnlAmount">Realized P&L in account currency; null for open trades.</param>
/// <param name="PnlCurrency">ISO-4217-like currency code for the P&L.</param>
/// <param name="Direction">Long or Short.</param>
/// <param name="Status">Open (still active) or Closed.</param>
/// <param name="Notes">Freeform notes. NEVER used to feed the AI advisor (prompt-injection defense).</param>
public sealed record ImportRow(
    int LineNumber,
    string? TicketId,
    string Symbol,
    decimal Volume,
    string VolumeCurrency,
    DateTimeOffset OpenedAt,
    DateTimeOffset? ClosedAt,
    decimal EntryPrice,
    decimal? ExitPrice,
    decimal? PnlAmount,
    string PnlCurrency,
    ImportDirection Direction,
    ImportRowStatus Status,
    string? Notes);