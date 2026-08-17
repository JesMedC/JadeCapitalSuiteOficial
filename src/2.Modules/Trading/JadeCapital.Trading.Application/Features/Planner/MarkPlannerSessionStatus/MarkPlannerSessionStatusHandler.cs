using JadeCapital.Trading.Application.Abstractions;
using JadeCapital.Trading.Domain.Common;
using JadeCapital.Shared.Kernel.Results;
using JadeCapital.Shared.Kernel.Time;
using JadeCapital.Trading.Application._Common;
using JadeCapital.Trading.Contracts.Planner;
using JadeCapital.Trading.Domain.Planner;
using MediatR;

namespace JadeCapital.Trading.Application.Features.Planner.MarkPlannerSessionStatus;

// ============================================================================
//  MarkPlannerSessionStatusCommand + Handler — slice 3c.
//
//  PATCH /api/planner/sessions/{id}/status  body = { newStatus: byte }
//
//  Cambia el status (Planned → Completed | Skipped | Cancelled). El aggregate
//  expone MarkCompleted / MarkSkipped / MarkCancelled (todos idempotentes);
//  este handler rutea segun el byte del wire.
//
//  Cross-user: la sesion debe pertenecer al req.UserId, si no NotFound.
// ============================================================================

public sealed record MarkPlannerSessionStatusCommand(
    Guid SessionId,
    Guid UserId,
    byte NewStatus) : IRequest<Result<PlannerSessionDto>>;

public sealed class MarkPlannerSessionStatusHandler
    : IRequestHandler<MarkPlannerSessionStatusCommand, Result<PlannerSessionDto>>
{
    private readonly IPlannerSessionRepository _sessions;
    private readonly IUnitOfWork _uow;
    private readonly IClock _clock;

    public MarkPlannerSessionStatusHandler(
        IPlannerSessionRepository sessions,
        IUnitOfWork uow,
        IClock clock)
    {
        _sessions = sessions;
        _uow = uow;
        _clock = clock;
    }

    public async Task<Result<PlannerSessionDto>> Handle(
        MarkPlannerSessionStatusCommand req,
        CancellationToken ct)
    {
        if (!IsValidStatus(req.NewStatus))
            return Result.Failure<PlannerSessionDto>(TradingDomainErrors.Planner.InvalidStatus);

        var session = await _sessions.GetByIdAsync(req.SessionId, ct);
        if (session is null || session.UserId != req.UserId)
            return Result.Failure<PlannerSessionDto>(TradingDomainErrors.Planner.NotFound);

        Result transitionResult = req.NewStatus switch
        {
            (byte)PlannerStatus.Completed => session.MarkCompleted(_clock),
            (byte)PlannerStatus.Skipped   => session.MarkSkipped(_clock),
            (byte)PlannerStatus.Cancelled => session.MarkCancelled(_clock),
            // Planned is technically a no-op (same as current default); treat as success.
            (byte)PlannerStatus.Planned   => Result.Success(),
            _ => Result.Failure(TradingDomainErrors.Planner.InvalidStatus),
        };

        if (transitionResult.IsFailure)
            return Result.Failure<PlannerSessionDto>(transitionResult.Error);

        await _sessions.UpdateAsync(session, ct);

        var saved = await _uow.SaveChangesAsync(ct);
        if (saved.IsFailure)
            return Result.Failure<PlannerSessionDto>(saved.Error);

        return Result.Success(session.ToDto());
    }

    private static bool IsValidStatus(byte b)
        => b is (byte)PlannerStatus.Planned
                or (byte)PlannerStatus.Completed
                or (byte)PlannerStatus.Skipped
                or (byte)PlannerStatus.Cancelled;
}