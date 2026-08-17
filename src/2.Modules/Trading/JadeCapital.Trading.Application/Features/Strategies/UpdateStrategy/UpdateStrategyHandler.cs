using FluentValidation;
using JadeCapital.Shared.Kernel.Results;
using JadeCapital.Shared.Kernel.Time;
using JadeCapital.Trading.Application.Abstractions;
using JadeCapital.Trading.Application._Common;
using JadeCapital.Trading.Contracts.Strategies;
using JadeCapital.Trading.Domain.Common;
using JadeCapital.Trading.Domain.Strategies;
using MediatR;

namespace JadeCapital.Trading.Application.Features.Strategies.UpdateStrategy;

// ============================================================================
//  UpdateStrategyCommand + Handler — slice 3a.
//
//  PATCH /api/strategies/{id}
//
//  Validations:
//   1. Cross-user: strategy must belong to req.UserId. Else NotFound.
//   2. Domain (Strategy.Update): mismas caps que Create.
//   3. Application: si cambia el name, ExistsByNameAsync para enforce
//      uniqueness activa (mismo row excluido via scope).
//      Duplicate → 409 conflict.
// ============================================================================

public sealed record UpdateStrategyCommand(
    Guid StrategyId,
    Guid UserId,
    string Name,
    string? Description,
    string? Symbol,
    byte? Timeframe,
    string? Rules) : IRequest<Result<StrategyDto>>;

public sealed class UpdateStrategyValidator : AbstractValidator<UpdateStrategyCommand>
{
    public UpdateStrategyValidator()
    {
        RuleFor(x => x.StrategyId).NotEqual(Guid.Empty);
        RuleFor(x => x.UserId).NotEqual(Guid.Empty);
        RuleFor(x => x.Name).NotEmpty().MaximumLength(Strategy.MaxNameLength);
        RuleFor(x => x.Description).MaximumLength(Strategy.MaxDescriptionLength);
        RuleFor(x => x.Rules).MaximumLength(Strategy.MaxRulesLength);
        RuleFor(x => x.Timeframe)
            .Must(t => !t.HasValue || (t.Value >= 1 && t.Value <= 9))
            .WithMessage("Timeframe must be between 1 and 9 (M1..MN) when present.");
    }
}

public sealed class UpdateStrategyHandler
    : IRequestHandler<UpdateStrategyCommand, Result<StrategyDto>>
{
    private readonly IStrategyRepository _strategies;
    private readonly IUnitOfWork _uow;
    private readonly IClock _clock;

    public UpdateStrategyHandler(
        IStrategyRepository strategies,
        IUnitOfWork uow,
        IClock clock)
    {
        _strategies = strategies;
        _uow = uow;
        _clock = clock;
    }

    public async Task<Result<StrategyDto>> Handle(
        UpdateStrategyCommand req,
        CancellationToken ct)
    {
        var strategy = await _strategies.GetByIdAsync(req.StrategyId, ct);
        if (strategy is null || strategy.UserId != req.UserId)
            return Result.Failure<StrategyDto>(TradingApplicationErrors.Strategies.NotFound);

        // Si cambia el name, chequea uniqueness activa excluyendo este mismo row.
        var nameChanged = !string.Equals(
            strategy.Name, req.Name.Trim(), StringComparison.OrdinalIgnoreCase);

        if (nameChanged)
        {
            var exists = await _strategies.ExistsByNameAsync(req.UserId, req.Name, ct);
            if (exists)
                return Result.Failure<StrategyDto>(TradingDomainErrors.Strategy.DuplicateName);
        }

        var timeframe = req.Timeframe.HasValue
            ? CreateStrategy.TimeframeMapper.FromByte(req.Timeframe.Value)
            : (Timeframe?)null;

        var updateResult = strategy.Update(
            name: req.Name,
            description: req.Description,
            symbol: req.Symbol,
            timeframe: timeframe,
            rules: req.Rules,
            clock: _clock);

        if (updateResult.IsFailure)
            return Result.Failure<StrategyDto>(updateResult.Error);

        await _strategies.UpdateAsync(strategy, ct);

        var saved = await _uow.SaveChangesAsync(ct);
        if (saved.IsFailure)
            return Result.Failure<StrategyDto>(saved.Error);

        return Result.Success(strategy.ToDto());
    }
}