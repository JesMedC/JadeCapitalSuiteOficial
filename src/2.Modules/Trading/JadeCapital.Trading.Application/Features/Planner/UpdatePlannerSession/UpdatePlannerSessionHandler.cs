using JadeCapital.Trading.Application.Abstractions;
using JadeCapital.Trading.Domain.Common;
using JadeCapital.Shared.Kernel.Results;
using JadeCapital.Shared.Kernel.Time;
using JadeCapital.Trading.Application._Common;
using JadeCapital.Trading.Contracts.Planner;
using JadeCapital.Trading.Domain.Planner;
using MediatR;

namespace JadeCapital.Trading.Application.Features.Planner.UpdatePlannerSession;

// ============================================================================
//  UpdatePlannerSessionCommand + Handler — slice 3c.
//
//  PATCH /api/planner/sessions/{id}
//
//  Updates planned_start_time, planned_end_time, symbol, notes. Status se
//  cambia via el endpoint dedicado PATCH /{id}/status (MarkPlannerSessionStatus).
//
//  Cross-user: la sesion debe pertenecer al req.UserId, si no NotFound.
//  Date NO se actualiza (move semantics: cambiar date = nueva sesion).
// ============================================================================

public sealed record UpdatePlannerSessionCommand(
    Guid SessionId,
    Guid UserId,
    TimeOnly? PlannedStartTime,
    TimeOnly? PlannedEndTime,
    string? Symbol,
    string? Notes) : IRequest<Result<PlannerSessionDto>>;

public sealed class UpdatePlannerSessionHandler
    : IRequestHandler<UpdatePlannerSessionCommand, Result<PlannerSessionDto>>
{
    private readonly IPlannerSessionRepository _sessions;
    private readonly IUnitOfWork _uow;
    private readonly IClock _clock;

    public UpdatePlannerSessionHandler(
        IPlannerSessionRepository sessions,
        IUnitOfWork uow,
        IClock clock)
    {
        _sessions = sessions;
        _uow = uow;
        _clock = clock;
    }

    public async Task<Result<PlannerSessionDto>> Handle(
        UpdatePlannerSessionCommand req,
        CancellationToken ct)
    {
        var session = await _sessions.GetByIdAsync(req.SessionId, ct);
        if (session is null || session.UserId != req.UserId)
            return Result.Failure<PlannerSessionDto>(TradingDomainErrors.Planner.NotFound);

        var updateResult = session.Update(
            plannedStartTime: req.PlannedStartTime,
            plannedEndTime: req.PlannedEndTime,
            symbol: req.Symbol,
            notes: req.Notes,
            clock: _clock);

        if (updateResult.IsFailure)
            return Result.Failure<PlannerSessionDto>(updateResult.Error);

        await _sessions.UpdateAsync(session, ct);

        var saved = await _uow.SaveChangesAsync(ct);
        if (saved.IsFailure)
            return Result.Failure<PlannerSessionDto>(saved.Error);

        return Result.Success(session.ToDto());
    }
}