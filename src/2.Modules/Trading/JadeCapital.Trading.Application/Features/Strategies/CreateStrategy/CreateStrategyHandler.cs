using FluentValidation;
using JadeCapital.Shared.Kernel.Results;
using JadeCapital.Shared.Kernel.Time;
using JadeCapital.Trading.Application.Abstractions;
using JadeCapital.Trading.Application._Common;
using JadeCapital.Trading.Contracts.Strategies;
using JadeCapital.Trading.Domain.Common;
using JadeCapital.Trading.Domain.Strategies;
using MediatR;

namespace JadeCapital.Trading.Application.Features.Strategies.CreateStrategy;

// ============================================================================
//  CreateStrategyCommand + Handler — slice 3a.
//
//  POST /api/strategies
//
//  Validations:
//   1. Domain (Strategy.Create): name 1..64, description <= 1000,
//      rules <= 2000, timeframe in {M1..MN} o null, symbol null or
//      non-empty. Errors mapean a 400 via ProblemFromResult.
//   2. Application (ExistsByNameAsync): unica active strategy por
//      (user, name) (case-insensitive). Duplicate → 409 conflict.
//
//  Cross-user: UserId viene SIEMPRE del JWT claim (lo pasa el endpoint),
//  nunca del body. La DB partial UNIQUE INDEX es la red de seguridad
//  contra races (dos POST simultaneos del mismo user con el mismo name).
// ============================================================================

public sealed record CreateStrategyCommand(
    Guid UserId,
    string Name,
    string? Description,
    string? Symbol,
    byte? Timeframe,
    string? Rules) : IRequest<Result<StrategyDto>>;

public sealed class CreateStrategyValidator : AbstractValidator<CreateStrategyCommand>
{
    public CreateStrategyValidator()
    {
        RuleFor(x => x.UserId).NotEqual(Guid.Empty);
        RuleFor(x => x.Name).NotEmpty().MaximumLength(Strategy.MaxNameLength);
        RuleFor(x => x.Description).MaximumLength(Strategy.MaxDescriptionLength);
        RuleFor(x => x.Rules).MaximumLength(Strategy.MaxRulesLength);
        RuleFor(x => x.Timeframe)
            .Must(t => !t.HasValue || (t.Value >= 1 && t.Value <= 9))
            .WithMessage("Timeframe must be between 1 and 9 (M1..MN) when present.");
    }
}

public sealed class CreateStrategyHandler
    : IRequestHandler<CreateStrategyCommand, Result<StrategyDto>>
{
    private readonly IStrategyRepository _strategies;
    private readonly IUnitOfWork _uow;
    private readonly IClock _clock;

    public CreateStrategyHandler(
        IStrategyRepository strategies,
        IUnitOfWork uow,
        IClock clock)
    {
        _strategies = strategies;
        _uow = uow;
        _clock = clock;
    }

    public async Task<Result<StrategyDto>> Handle(
        CreateStrategyCommand req,
        CancellationToken ct)
    {
        var timeframe = req.Timeframe.HasValue
            ? TimeframeMapper.FromByte(req.Timeframe.Value)
            : (Timeframe?)null;

        var createResult = Strategy.Create(
            userId: req.UserId,
            name: req.Name,
            description: req.Description,
            symbol: req.Symbol,
            timeframe: timeframe,
            rules: req.Rules,
            clock: _clock);

        if (createResult.IsFailure)
            return Result.Failure<StrategyDto>(createResult.Error);

        var strategy = createResult.Value;

        // Uniqueness active (case-insensitive). Existence check antes de
        // AddAsync — la partial UNIQUE INDEX en DB es la red de seguridad
        // contra races (dos POST concurrentes del mismo user+name).
        var exists = await _strategies.ExistsByNameAsync(
            req.UserId, strategy.Name, ct);
        if (exists)
            return Result.Failure<StrategyDto>(TradingDomainErrors.Strategy.DuplicateName);

        await _strategies.AddAsync(strategy, ct);

        var saved = await _uow.SaveChangesAsync(ct);
        if (saved.IsFailure)
            return Result.Failure<StrategyDto>(saved.Error);

        return Result.Success(strategy.ToDto());
    }
}

/// <summary>
/// Mapper entre el byte del wire (1..9) y la enum Timeframe del dominio.
/// Si el validator dejo pasar un byte invalido (deberia ser imposible),
/// devuelve <see cref="Timeframe.Unspecified"/> como fallback para que
/// el handler rechace via Strategy.Create con InvalidTimeframe.
/// </summary>
public static class TimeframeMapper
{
    public static Timeframe FromByte(byte b)
        => b switch
        {
            1 => Timeframe.M1,
            2 => Timeframe.M5,
            3 => Timeframe.M15,
            4 => Timeframe.M30,
            5 => Timeframe.H1,
            6 => Timeframe.H4,
            7 => Timeframe.D1,
            8 => Timeframe.W1,
            9 => Timeframe.MN,
            _ => Timeframe.Unspecified,
        };
}