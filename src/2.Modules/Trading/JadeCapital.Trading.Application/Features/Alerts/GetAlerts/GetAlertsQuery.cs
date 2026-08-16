using JadeCapital.Trading.Application._Common;
using JadeCapital.Trading.Contracts.Alerts;
using JadeCapital.Shared.Kernel.Alerts;
using JadeCapital.Shared.Kernel.Results;
using JadeCapital.Trading.Application.Abstractions;
using MediatR;

namespace JadeCapital.Trading.Application.Features.Alerts.GetAlerts;

// ============================================================================
//  GetAlertsQuery — slice 3b (Trader Strategies + Alerts + Planner).
//
//  GET /api/alerts?activeOnly=true|false
//
//  Returns alerts for the calling user. Default = activeOnly = false
//  (full audit trail per spec). The repository handles the activeOnly
//  filter at the SQL layer (acknowledged_at IS NULL AND expires_at
//  filter + activeOnly boolean).
//
//  Cross-user scope: IAlertRepository.ListByUserAsync always filters
//  by userId from the JWT claim (the endpoint extracts it).
// ============================================================================

public sealed record GetAlertsQuery(
    Guid UserId,
    bool ActiveOnly) : IRequest<Result<IReadOnlyList<AlertDto>>>;

public sealed class GetAlertsHandler
    : IRequestHandler<GetAlertsQuery, Result<IReadOnlyList<AlertDto>>>
{
    private readonly IAlertRepository _alerts;
    private readonly JadeCapital.Shared.Kernel.Time.IClock _clock;

    public GetAlertsHandler(IAlertRepository alerts, JadeCapital.Shared.Kernel.Time.IClock clock)
    {
        _alerts = alerts;
        _clock = clock;
    }

    public async Task<Result<IReadOnlyList<AlertDto>>> Handle(
        GetAlertsQuery req, CancellationToken ct)
    {
        var now = _clock.UtcNow;
        var items = await _alerts.ListByUserAsync(req.UserId, req.ActiveOnly, now, ct);
        var dtos = items.Select(AlertMapping.ToDto).ToList();
        return Result.Success<IReadOnlyList<AlertDto>>(dtos);
    }
}