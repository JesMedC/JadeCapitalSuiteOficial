using FluentValidation;
using JadeCapital.Shared.Kernel.Results;
using JadeCapital.Trading.Application.Abstractions;
using JadeCapital.Trading.Application._Common;
using JadeCapital.Trading.Contracts.Strategies;
using MediatR;

namespace JadeCapital.Trading.Application.Features.Strategies.ListStrategies;

// ============================================================================
//  ListStrategiesQuery + Handler — slice 3a.
//
//  GET /api/strategies?activeOnly=
//
//  Lista strategies del user. Si activeOnly=true (default), filtra
//  WHERE is_active=true. Si false, incluye soft-deleted (caso admin/futuro).
//
//  Cross-user: UserId viene del JWT claim. El repo filtra WHERE user_id = @userId.
// ============================================================================

public sealed record ListStrategiesQuery(
    Guid UserId,
    bool ActiveOnly = true) : IRequest<Result<IReadOnlyList<StrategyDto>>>;

public sealed class ListStrategiesValidator : AbstractValidator<ListStrategiesQuery>
{
    public ListStrategiesValidator()
    {
        RuleFor(x => x.UserId).NotEqual(Guid.Empty);
    }
}

public sealed class ListStrategiesHandler
    : IRequestHandler<ListStrategiesQuery, Result<IReadOnlyList<StrategyDto>>>
{
    private readonly IStrategyRepository _strategies;

    public ListStrategiesHandler(IStrategyRepository strategies)
    {
        _strategies = strategies;
    }

    public async Task<Result<IReadOnlyList<StrategyDto>>> Handle(
        ListStrategiesQuery req,
        CancellationToken ct)
    {
        var list = await _strategies.ListByUserAsync(req.UserId, req.ActiveOnly, ct);
        return Result.Success<IReadOnlyList<StrategyDto>>(
            list.Select(s => s.ToDto()).ToArray());
    }
}