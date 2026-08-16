using FluentValidation;
using JadeCapital.Shared.Kernel.Results;
using JadeCapital.Trading.Application.Abstractions;
using JadeCapital.Trading.Application._Common;
using JadeCapital.Trading.Contracts.Strategies;
using MediatR;

namespace JadeCapital.Trading.Application.Features.Strategies.GetStrategyAnalytics;

// ============================================================================
//  GetStrategyAnalyticsQuery + Handler — slice 3a.
//
//  GET /api/strategies/{id}/analytics
//
//  Cross-user: strategy must belong to req.UserId. Else NotFound.
//  Aggregate se calcula on-read sobre los trades cerrados del user que
//  tienen strategy_id = X (el repo encapsula el SQL aggregate).
// ============================================================================

public sealed record GetStrategyAnalyticsQuery(
    Guid StrategyId,
    Guid UserId) : IRequest<Result<StrategyAnalyticsDto>>;

public sealed class GetStrategyAnalyticsValidator : AbstractValidator<GetStrategyAnalyticsQuery>
{
    public GetStrategyAnalyticsValidator()
    {
        RuleFor(x => x.StrategyId).NotEqual(Guid.Empty);
        RuleFor(x => x.UserId).NotEqual(Guid.Empty);
    }
}

public sealed class GetStrategyAnalyticsHandler
    : IRequestHandler<GetStrategyAnalyticsQuery, Result<StrategyAnalyticsDto>>
{
    private readonly IStrategyRepository _strategies;

    public GetStrategyAnalyticsHandler(IStrategyRepository strategies)
    {
        _strategies = strategies;
    }

    public async Task<Result<StrategyAnalyticsDto>> Handle(
        GetStrategyAnalyticsQuery req,
        CancellationToken ct)
    {
        // Ownership check ANTES del aggregate: una strategy de otro user
        // mapea a NotFound (no leak existencia).
        var strategy = await _strategies.GetByIdAsync(req.StrategyId, ct);
        if (strategy is null || strategy.UserId != req.UserId)
            return Result.Failure<StrategyAnalyticsDto>(
                TradingApplicationErrors.Strategies.NotFound);

        var analytics = await _strategies.GetAnalyticsAsync(
            req.UserId, req.StrategyId, ct);

        return Result.Success(analytics);
    }
}