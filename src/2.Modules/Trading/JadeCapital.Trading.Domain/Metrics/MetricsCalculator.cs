using JadeCapital.Shared.Kernel.Money;
using JadeCapital.Trading.Domain.Enums;
using JadeCapital.Trading.Domain.Trades;

namespace JadeCapital.Trading.Domain.Metrics;

/// <summary>
/// Resultado puro del calculo de metricas. NO es un DTO de API — el handler
/// lo proyecta a MetricsDto (en Application) con campos adicionales.
/// Inmutable. Toda operacion es deterministica dado el mismo input.
/// </summary>
public sealed record MetricsResult(
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
    IReadOnlyList<EquityPoint> EquityCurve,
    IReadOnlyList<SymbolStat> SymbolStats,
    string Currency);

public sealed record EquityPoint(
    DateTimeOffset Timestamp,
    decimal Equity,
    decimal Drawdown);

public sealed record SymbolStat(
    string Symbol,
    int Trades,
    decimal TotalPnl,
    decimal WinRate);

/// <summary>
/// Calculo puro de metricas. Stateless. Determinista.
/// Inputs: trades del usuario en la ventana (pueden ser Open + Closed) +
/// conteos por estado + reloj (para timestamps estables en tests).
/// Output: MetricsResult con todos los campos requeridos por la spec.
///
/// El handler se encarga de:
///   1. Filtrar por periodo (llamando a IMetricsQueryStore).
///   2. Contar Open / Closed / Total.
///   3. Pasar esos conteos + la lista al calculator.
/// Asi el calculator no depende de EF ni de la DB — es 100% testeable.
/// </summary>
public static class MetricsCalculator
{
    /// <summary>
    /// Calcula las metricas sobre la lista de trades del usuario en la ventana.
    /// La lista puede contener Open + Closed; los calculos solo usan Closed.
    /// </summary>
    public static MetricsResult Compute(
        IReadOnlyList<Trade> trades,
        int totalOpen,
        int totalClosed,
        int totalTrades,
        DateTimeOffset? fromPeriod,
        DateTimeOffset nowUtc)
    {
        // Filtramos Closed con PnL seteado (defensa contra filas inconsistentes).
        var closed = trades
            .Where(t => t.Status == TradeStatus.Closed && t.PnL is not null)
            .OrderBy(t => t.OpenedAt)
            .ToList();

        var closedCount = closed.Count;
        var totalClosedReported = totalClosed > 0 ? totalClosed : closedCount;
        var totalOpenReported = totalOpen;
        var totalReported = totalTrades > 0 ? totalTrades : (totalClosedReported + totalOpenReported);

        if (closedCount == 0)
        {
            return new MetricsResult(
                TotalTrades: totalReported,
                TotalClosedTrades: totalClosedReported,
                TotalOpenTrades: totalOpenReported,
                WinRate: 0m,
                Expectancy: 0m,
                ProfitFactor: 0m,
                Payoff: 0m,
                Sqn: 0m,
                MaxDrawdown: 0m,
                MaxDrawdownAmount: 0m,
                MaxDrawdownPercent: 0m,
                EquityCurve: Array.Empty<EquityPoint>(),
                SymbolStats: Array.Empty<SymbolStat>(),
                Currency: Currency.Usd.Code);
        }

        var pnlValues = closed.Select(t => t.PnL!.Amount).ToArray();
        var grossWins = pnlValues.Where(p => p > 0m).Sum();
        var grossLosses = Math.Abs(pnlValues.Where(p => p < 0m).Sum());
        var winsCount = pnlValues.Count(p => p > 0m);
        var lossesCount = pnlValues.Count(p => p < 0m);

        // ===== WinRate =====
        var winRate = closedCount > 0
            ? Math.Round((decimal)winsCount / closedCount * 100m, 4, MidpointRounding.AwayFromZero)
            : 0m;

        // ===== Expectancy = (grossWins - grossLosses) / closedCount =====
        var expectancy = closedCount > 0
            ? Math.Round((grossWins - grossLosses) / closedCount, 4, MidpointRounding.AwayFromZero)
            : 0m;

        // ===== Profit Factor = grossWins / grossLosses (or 0 if no losses) =====
        var profitFactor = grossLosses > 0m
            ? Math.Round(grossWins / grossLosses, 4, MidpointRounding.AwayFromZero)
            : 0m;

        // ===== Payoff = avgWin / avgLoss (or 0 if no losses) =====
        var avgWin = winsCount > 0 ? grossWins / winsCount : 0m;
        var avgLoss = lossesCount > 0 ? grossLosses / lossesCount : 0m;
        var payoff = avgLoss > 0m
            ? Math.Round(avgWin / avgLoss, 4, MidpointRounding.AwayFromZero)
            : 0m;

        // ===== SQN = sqrt(N) * mean / stdev  (N = closedCount) =====
        var sqn = CalculateSqn(closedCount, pnlValues);

        // ===== Equity curve + max drawdown =====
        var (curve, maxDdAmount) = BuildEquityCurveAndMaxDrawdown(closed);
        var peakAtMaxDd = ComputePeakAtMaxDrawdown(curve, maxDdAmount);
        var maxDdPercent = peakAtMaxDd > 0m
            ? Math.Round(maxDdAmount / peakAtMaxDd * 100m, 4, MidpointRounding.AwayFromZero)
            : 0m;

        // ===== Symbol stats =====
        var symbolStats = BuildSymbolStats(closed);

        return new MetricsResult(
            TotalTrades: totalReported,
            TotalClosedTrades: totalClosedReported,
            TotalOpenTrades: totalOpenReported,
            WinRate: winRate,
            Expectancy: expectancy,
            ProfitFactor: profitFactor,
            Payoff: payoff,
            Sqn: sqn,
            MaxDrawdown: maxDdAmount,
            MaxDrawdownAmount: maxDdAmount,
            MaxDrawdownPercent: maxDdPercent,
            EquityCurve: curve,
            SymbolStats: symbolStats,
            Currency: Currency.Usd.Code);
    }

    private static decimal CalculateSqn(int closedCount, decimal[] pnlValues)
    {
        if (closedCount < 2) return 0m;

        var mean = pnlValues.Sum() / closedCount;
        var variance = pnlValues.Sum(p => (p - mean) * (p - mean)) / (closedCount - 1);
        var stdev = (decimal)Math.Sqrt((double)variance);
        if (stdev <= 0m) return 0m;

        var sqn = (decimal)Math.Sqrt((double)closedCount) * mean / stdev;
        return Math.Round(sqn, 4, MidpointRounding.AwayFromZero);
    }

    private static (IReadOnlyList<EquityPoint> Curve, decimal MaxDrawdownAmount) BuildEquityCurveAndMaxDrawdown(
        List<Trade> closedOrdered)
    {
        var points = new List<EquityPoint>(closedOrdered.Count);
        decimal running = 0m;
        decimal peak = 0m;
        decimal maxDd = 0m;

        foreach (var trade in closedOrdered)
        {
            running += trade.PnL!.Amount;
            if (running > peak) peak = running;
            var drawdown = running - peak; // <= 0 (running <= peak)
            points.Add(new EquityPoint(trade.OpenedAt, Math.Round(running, 4), drawdown));
            if (drawdown < maxDd) maxDd = drawdown;
        }

        return (points, Math.Round(maxDd, 4, MidpointRounding.AwayFromZero));
    }

    private static decimal ComputePeakAtMaxDrawdown(IReadOnlyList<EquityPoint> curve, decimal maxDdAmount)
    {
        if (maxDdAmount >= 0m) return 0m;
        // Busca el primer punto cuyo drawdown == maxDd y devuelve el peak hasta ese punto.
        decimal peak = 0m;
        foreach (var p in curve)
        {
            if (p.Equity > peak) peak = p.Equity;
            if (p.Drawdown == maxDdAmount) return peak;
        }
        return peak;
    }

    private static IReadOnlyList<SymbolStat> BuildSymbolStats(List<Trade> closedOrdered)
    {
        var groups = closedOrdered
            .GroupBy(t => t.Symbol.Value)
            .Select(g =>
            {
                var trades = g.ToList();
                var wins = trades.Count(t => t.PnL!.Amount > 0m);
                var net = trades.Sum(t => t.PnL!.Amount);
                var winRate = trades.Count > 0
                    ? Math.Round((decimal)wins / trades.Count * 100m, 4, MidpointRounding.AwayFromZero)
                    : 0m;
                return new SymbolStat(
                    Symbol: g.Key,
                    Trades: trades.Count,
                    TotalPnl: Math.Round(net, 4),
                    WinRate: winRate);
            })
            .OrderByDescending(s => s.TotalPnl)
            .ToList();

        return groups;
    }
}
