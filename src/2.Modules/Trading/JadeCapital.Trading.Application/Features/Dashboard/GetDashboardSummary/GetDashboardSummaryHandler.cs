using JadeCapital.Shared.Kernel.Results;
using JadeCapital.Trading.Application.Abstractions;
using JadeCapital.Trading.Application._Common;
using JadeCapital.Trading.Domain.Enums;
using MediatR;

namespace JadeCapital.Trading.Application.Features.Dashboard.GetDashboardSummary;

/// <summary>
/// Calcula KPIs del usuario en un rango temporal [From, To):
/// - conteos por estado (Open/Closed, con Cancelled ignorado para win-rate).
/// - Wins/Losses (trades cerrados con PnL &gt; 0 / &lt; 0).
/// - WinRate, TotalPnL, Best/Worst/Avg trade.
///
/// Asume que el usuario opera en una sola moneda quotable (v1). Currency
/// del summary se toma del Volume del primer trade (todos los trades del
/// user deberian compartir Volume.Currency en account-moneda en v1).
/// </summary>
public sealed class GetDashboardSummaryHandler
    : IRequestHandler<GetDashboardSummaryQuery, Result<DashboardSummaryDto>>
{
    private readonly ITradeRepository _trades;

    public GetDashboardSummaryHandler(ITradeRepository trades)
    {
        _trades = trades;
    }

    public async Task<Result<DashboardSummaryDto>> Handle(GetDashboardSummaryQuery req, CancellationToken ct)
    {
        var trades = await _trades.ListByUserIdAndOpenedAtRangeAsync(
            req.UserId, req.From, req.To, ct);

        var totalCount = trades.Count;
        var openCount = 0;
        var closedCount = 0;
        var winsCount = 0;
        var lossesCount = 0;
        decimal totalPnL = 0m;
        decimal? bestTrade = null;
        decimal? worstTrade = null;
        var currencyCaptured = false;
        string currency = "USD";

        foreach (var trade in trades)
        {
            // Capturamos la currency del primer trade para el reporte.
            // En v1 todos los trades de un user comparten Volume.Currency.
            if (!currencyCaptured)
            {
                currency = trade.Volume.Currency.Code;
                currencyCaptured = true;
            }

            switch (trade.Status)
            {
                case TradeStatus.Open:
                    openCount++;
                    break;
                case TradeStatus.Closed:
                    closedCount++;
                    if (trade.PnL is not null)
                    {
                        var pnl = trade.PnL.Amount;
                        totalPnL += pnl;
                        if (pnl > 0m) winsCount++;
                        else if (pnl < 0m) lossesCount++;

                        if (bestTrade is null || pnl > bestTrade.Value) bestTrade = pnl;
                        if (worstTrade is null || pnl < worstTrade.Value) worstTrade = pnl;
                    }
                    break;
                case TradeStatus.Cancelled:
                    // Los cancelados no cuentan para win-rate (operacion nunca se efectuo).
                    break;
            }
        }

        var winRate = closedCount > 0
            ? Math.Round((decimal)winsCount / closedCount, 4, MidpointRounding.AwayFromZero)
            : 0m;

        var avgTrade = closedCount > 0
            ? Math.Round(totalPnL / closedCount, 4, MidpointRounding.AwayFromZero)
            : 0m;

        var dto = new DashboardSummaryDto(
            TotalCount: totalCount,
            OpenCount: openCount,
            ClosedCount: closedCount,
            WinsCount: winsCount,
            LossesCount: lossesCount,
            WinRate: winRate,
            TotalPnL: Math.Round(totalPnL, 4, MidpointRounding.AwayFromZero),
            BestTrade: bestTrade ?? 0m,
            WorstTrade: worstTrade ?? 0m,
            AvgTrade: avgTrade,
            Currency: currency);

        return Result.Success(dto);
    }
}