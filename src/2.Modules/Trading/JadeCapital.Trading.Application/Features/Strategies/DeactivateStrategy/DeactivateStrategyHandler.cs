using FluentValidation;
using JadeCapital.Shared.Kernel.Results;
using JadeCapital.Shared.Kernel.Time;
using JadeCapital.Trading.Application.Abstractions;
using JadeCapital.Trading.Application._Common;
using MediatR;

namespace JadeCapital.Trading.Application.Features.Strategies.DeactivateStrategy;

// ============================================================================
//  DeactivateStrategyCommand + Handler — slice 3a.
//
//  DELETE /api/strategies/{id}
//
//  Soft-delete: flipea IsActive=false via Strategy.Deactivate.
//  Idempotente (segunda llamada no produce error).
//
//  Cross-user: strategy must belong to req.UserId. Else NotFound.
// ============================================================================

public sealed record DeactivateStrategyCommand(
    Guid StrategyId,
    Guid UserId) : IRequest<Result>;

public sealed class DeactivateStrategyValidator : AbstractValidator<DeactivateStrategyCommand>
{
    public DeactivateStrategyValidator()
    {
        RuleFor(x => x.StrategyId).NotEqual(Guid.Empty);
        RuleFor(x => x.UserId).NotEqual(Guid.Empty);
    }
}

public sealed class DeactivateStrategyHandler
    : IRequestHandler<DeactivateStrategyCommand, Result>
{
    private readonly IStrategyRepository _strategies;
    private readonly IUnitOfWork _uow;
    private readonly IClock _clock;

    public DeactivateStrategyHandler(
        IStrategyRepository strategies,
        IUnitOfWork uow,
        IClock clock)
    {
        _strategies = strategies;
        _uow = uow;
        _clock = clock;
    }

    public async Task<Result> Handle(
        DeactivateStrategyCommand req,
        CancellationToken ct)
    {
        var strategy = await _strategies.GetByIdAsync(req.StrategyId, ct);
        if (strategy is null || strategy.UserId != req.UserId)
            return Result.Failure(TradingApplicationErrors.Strategies.NotFound);

        var deact = strategy.Deactivate(_clock);
        if (deact.IsFailure)
            return Result.Failure(deact.Error);

        await _strategies.UpdateAsync(strategy, ct);

        var saved = await _uow.SaveChangesAsync(ct);
        if (saved.IsFailure)
            return Result.Failure(saved.Error);

        return Result.Success();
    }
}