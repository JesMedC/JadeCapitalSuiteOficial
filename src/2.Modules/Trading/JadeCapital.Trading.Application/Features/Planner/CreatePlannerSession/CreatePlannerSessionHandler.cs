using JadeCapital.Trading.Application.Abstractions;
using JadeCapital.Trading.Domain.Common;
using JadeCapital.Shared.Kernel.Results;
using JadeCapital.Shared.Kernel.Time;
using JadeCapital.Trading.Application._Common;
using JadeCapital.Trading.Contracts.Planner;
using JadeCapital.Trading.Domain.Planner;
using MediatR;

namespace JadeCapital.Trading.Application.Features.Planner.CreatePlannerSession;

// ============================================================================
//  CreatePlannerSessionCommand + Handler — slice 3c.
//
//  POST /api/planner/sessions
//
//  Validations:
//   1. Domain (PlannerSession.Create): userId non-empty, end > start si
//      ambos presentes, notes <= 500.
//   2. Application (ExistsForDateAsync): unica sesion por (user, date).
//      Duplicate → 409 conflict.
//
//  Cross-user: UserId viene SIEMPRE del JWT claim (lo pasa el endpoint),
//  nunca del body. La UNIQUE INDEX en DB (ux_planner_user_date) es la red
//  de seguridad contra races (dos POST simultaneos del mismo user+date).
// ============================================================================

public sealed record CreatePlannerSessionCommand(
    Guid UserId,
    LocalDate SessionDate,
    TimeOnly? PlannedStartTime,
    TimeOnly? PlannedEndTime,
    string? Symbol,
    string? Notes) : IRequest<Result<PlannerSessionDto>>;

public sealed class CreatePlannerSessionHandler
    : IRequestHandler<CreatePlannerSessionCommand, Result<PlannerSessionDto>>
{
    private readonly IPlannerSessionRepository _sessions;
    private readonly IUnitOfWork _uow;
    private readonly IClock _clock;

    public CreatePlannerSessionHandler(
        IPlannerSessionRepository sessions,
        IUnitOfWork uow,
        IClock clock)
    {
        _sessions = sessions;
        _uow = uow;
        _clock = clock;
    }

    public async Task<Result<PlannerSessionDto>> Handle(
        CreatePlannerSessionCommand req,
        CancellationToken ct)
    {
        // Domain validation first — same caps / range as the aggregate enforces.
        var createResult = PlannerSession.Create(
            userId: req.UserId,
            sessionDate: req.SessionDate,
            plannedStartTime: req.PlannedStartTime,
            plannedEndTime: req.PlannedEndTime,
            symbol: req.Symbol,
            notes: req.Notes,
            clock: _clock);

        if (createResult.IsFailure)
            return Result.Failure<PlannerSessionDto>(createResult.Error);

        var session = createResult.Value;

        // Uniqueness check: exactly-one-per-(user, date). El UNIQUE INDEX en
        // la DB es la red de seguridad contra races.
        var exists = await _sessions.ExistsForDateAsync(
            req.UserId, req.SessionDate, ct);
        if (exists)
            return Result.Failure<PlannerSessionDto>(TradingDomainErrors.Planner.AlreadyExistsForDate);

        await _sessions.AddAsync(session, ct);

        var saved = await _uow.SaveChangesAsync(ct);
        if (saved.IsFailure)
            return Result.Failure<PlannerSessionDto>(saved.Error);

        return Result.Success(session.ToDto());
    }
}