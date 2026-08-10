using JadeCapital.Shared.Kernel.Results;
using JadeCapital.Trading.Application.Abstractions;
using JadeCapital.Trading.Application._Common;
using JadeCapital.Trading.Domain.Enums;
using MediatR;

namespace JadeCapital.Trading.Application.Features.Dashboard.GetPnlCalendar;

/// <summary>
/// Genera el calendario mensual de PnL: un punto por dia del mes con la
/// suma de PnL de trades CERRADOS ese dia (OpenedAt se ignora, usamos
/// ClosedAt para que un trade abierto el 30 y cerrado el 2 del mes
/// siguiente cuente en el mes del cierre, donde realmente impacta el PnL).
/// </summary>
public sealed class GetPnlCalendarHandler
    : IRequestHandler<GetPnlCalendarQuery, Result<CalendarDto>>
{
    private readonly ITradeRepository _trades;

    public GetPnlCalendarHandler(ITradeRepository trades)
    {
        _trades = trades;
    }

    public async Task<Result<CalendarDto>> Handle(GetPnlCalendarQuery req, CancellationToken ct)
    {
        var from = new DateTimeOffset(req.Year, req.Month, 1, 0, 0, 0, TimeSpan.Zero);
        var to = from.AddMonths(1);

        var trades = await _trades.ListByUserIdAndOpenedAtRangeAsync(
            req.UserId, from, to, ct);

        var byDay = new Dictionary<DateOnly, (decimal Pnl, int Count)>();

        foreach (var trade in trades)
        {
            if (trade.Status != TradeStatus.Closed || trade.ClosedAt is null || trade.PnL is null)
                continue;

            var day = DateOnly.FromDateTime(trade.ClosedAt.Value.UtcDateTime);
            if (byDay.TryGetValue(day, out var current))
                byDay[day] = (current.Pnl + trade.PnL.Amount, current.Count + 1);
            else
                byDay[day] = (trade.PnL.Amount, 1);
        }

        var days = byDay
            .OrderBy(kv => kv.Key)
            .Select(kv => new CalendarDayDto(
                kv.Key,
                Math.Round(kv.Value.Pnl, 4, MidpointRounding.AwayFromZero),
                kv.Value.Count))
            .ToList();

        return Result.Success(new CalendarDto(req.Year, req.Month, days));
    }
}