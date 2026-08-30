using JadeCapital.Trading.Application.Abstractions;
using JadeCapital.Trading.Domain.Common;
using JadeCapital.Shared.Kernel.Results;
using JadeCapital.Trading.Application._Common;
using JadeCapital.Trading.Contracts.Planner;
using JadeCapital.Trading.Domain.Planner;
using MediatR;

namespace JadeCapital.Trading.Application.Features.Planner.GetPlannerSessionById;

// ============================================================================
//  GetPlannerSessionByIdQuery + Handler — slice 3c.
//
//  GET /api/planner/sessions/{id}
//
//  Returns the session if it belongs to the calling user. Cross-user reads
//  collapse to NotFound (no leak existence).
// ============================================================================

public sealed record GetPlannerSessionByIdQuery(
    Guid UserId,
    Guid SessionId) : IRequest<Result<PlannerSessionDto>>;

public sealed class GetPlannerSessionByIdHandler
    : IRequestHandler<GetPlannerSessionByIdQuery, Result<PlannerSessionDto>>
{
    private readonly IPlannerSessionRepository _sessions;

    public GetPlannerSessionByIdHandler(IPlannerSessionRepository sessions)
    {
        _sessions = sessions;
    }

    public async Task<Result<PlannerSessionDto>> Handle(
        GetPlannerSessionByIdQuery req,
        CancellationToken ct)
    {
        var session = await _sessions.GetByIdAsync(req.SessionId, ct);
        if (session is null || session.UserId != req.UserId)
            return Result.Failure<PlannerSessionDto>(TradingDomainErrors.Planner.NotFound);

        return Result.Success(session.ToDto());
    }
}