using JadeCapital.Trading.Application._Common;
using JadeCapital.Trading.Contracts.Alerts;
using JadeCapital.Shared.Kernel.Results;
using JadeCapital.Trading.Application.Abstractions;
using JadeCapital.Trading.Domain.Common;
using MediatR;

namespace JadeCapital.Trading.Application.Features.Alerts.GetAlertById;

// ============================================================================
//  GetAlertByIdQuery — slice 3b (Trader Strategies + Alerts + Planner).
//
//  GET /api/alerts/{id}
//
//  Returns a single alert by id. Cross-user scope: the repository filters
//  by userId so a user can't read another user's alert (404, not 403 —
//  per spec "User B reads user A's alert" scenario).
// ============================================================================

public sealed record GetAlertByIdQuery(Guid UserId, Guid AlertId)
    : IRequest<Result<JadeCapital.Trading.Contracts.Alerts.AlertDto>>;

public sealed class GetAlertByIdHandler
    : IRequestHandler<GetAlertByIdQuery, Result<JadeCapital.Trading.Contracts.Alerts.AlertDto>>
{
    private readonly IAlertRepository _alerts;

    public GetAlertByIdHandler(IAlertRepository alerts) { _alerts = alerts; }

    public async Task<Result<JadeCapital.Trading.Contracts.Alerts.AlertDto>> Handle(
        GetAlertByIdQuery req, CancellationToken ct)
    {
        var alert = await _alerts.GetByIdAsync(req.AlertId, req.UserId, ct);
        if (alert is null)
            return Result.Failure<JadeCapital.Trading.Contracts.Alerts.AlertDto>(TradingDomainErrors.Alert.NotFound);

        return Result.Success(AlertMapping.ToDto(alert));
    }
}