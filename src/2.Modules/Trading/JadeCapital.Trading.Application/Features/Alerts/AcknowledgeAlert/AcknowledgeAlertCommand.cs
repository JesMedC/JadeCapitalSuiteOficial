using JadeCapital.Trading.Application._Common;
using JadeCapital.Trading.Contracts.Alerts;
using JadeCapital.Shared.Kernel.Results;
using JadeCapital.Trading.Application.Abstractions;
using JadeCapital.Trading.Domain.Common;
using MediatR;

namespace JadeCapital.Trading.Application.Features.Alerts.AcknowledgeAlert;

// ============================================================================
//  AcknowledgeAlertCommand — slice 3b (Trader Strategies + Alerts + Planner).
//
//  PATCH /api/alerts/{id}/ack
//
//  Marks the alert as acknowledged. Idempotent: a second call returns
//  the alert unchanged (per spec scenario "Ack idempotent"). Returns
//  404 when the alert doesn't exist or belongs to another user.
// ============================================================================

public sealed record AcknowledgeAlertCommand(Guid UserId, Guid AlertId)
    : IRequest<Result<JadeCapital.Trading.Contracts.Alerts.AlertDto>>;

public sealed class AcknowledgeAlertHandler
    : IRequestHandler<AcknowledgeAlertCommand, Result<JadeCapital.Trading.Contracts.Alerts.AlertDto>>
{
    private readonly IAlertRepository _alerts;
    private readonly JadeCapital.Shared.Kernel.Time.IClock _clock;
    private readonly IUnitOfWork _uow;

    public AcknowledgeAlertHandler(
        IAlertRepository alerts,
        JadeCapital.Shared.Kernel.Time.IClock clock,
        IUnitOfWork uow)
    {
        _alerts = alerts;
        _clock = clock;
        _uow = uow;
    }

    public async Task<Result<JadeCapital.Trading.Contracts.Alerts.AlertDto>> Handle(
        AcknowledgeAlertCommand req, CancellationToken ct)
    {
        var alert = await _alerts.GetByIdAsync(req.AlertId, req.UserId, ct);
        if (alert is null)
            return Result.Failure<JadeCapital.Trading.Contracts.Alerts.AlertDto>(TradingDomainErrors.Alert.NotFound);

        var ackResult = alert.Acknowledge(_clock);
        if (ackResult.IsFailure)
            return Result.Failure<JadeCapital.Trading.Contracts.Alerts.AlertDto>(ackResult.Error);

        await _alerts.UpdateAsync(alert, ct);
        var saved = await _uow.SaveChangesAsync(ct);
        if (saved.IsFailure)
            return Result.Failure<JadeCapital.Trading.Contracts.Alerts.AlertDto>(saved.Error);

        return Result.Success(AlertMapping.ToDto(alert));
    }
}