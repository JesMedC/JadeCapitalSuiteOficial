namespace JadeCapital.Trading.Application._Common;

/// <summary>
/// Snapshot server-side de las metricas de trading del usuario en una ventana.
/// Calculado en cada request (read-model, no cache). NO se persiste.
///
/// El frontend (analytics.page.ts) consume este DTO directamente: la curva de
/// equity viene del server, los KPIs vienen del server, los stats por simbolo
/// vienen del server. NO se recomputa nada en el cliente.
///
/// Money invariants: expectancy, profit factor, payoff, sqn, drawdowns y
/// PnL de symbol stats son TODOS decimal end-to-end para evitar perdida de
/// precision. La columna destino es NUMERIC(24,8) en Postgres; el .NET
/// decimal cabe de sobra y el dominio ya valida &lt; 10^16.
/// </summary>
public sealed record MetricsDto(
    string Period,
    int TotalTrades,
    int TotalClosedTrades,
    int TotalOpenTrades,
    decimal WinRate,
    decimal Expectancy,
    decimal ProfitFactor,
    decimal Payoff,
    decimal Sqn,
    decimal MaxDrawdown,
    decimal MaxDrawdownAmount,
    decimal MaxDrawdownPercent,
    IReadOnlyList<EquityPointDto> EquityCurve,
    IReadOnlyList<SymbolStatDto> SymbolStats,
    string Currency);

public sealed record EquityPointDto(
    DateTimeOffset Timestamp,
    decimal Equity,
    decimal Drawdown);

public sealed record SymbolStatDto(
    string Symbol,
    int Trades,
    decimal TotalPnl,
    decimal WinRate);
