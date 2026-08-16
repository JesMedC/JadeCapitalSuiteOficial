using FluentValidation;
using JadeCapital.Shared.Kernel.Results;
using JadeCapital.Trading.Application.Abstractions;
using JadeCapital.Trading.Application._Common;
using JadeCapital.Trading.Contracts.Strategies;
using MediatR;

namespace JadeCapital.Trading.Application.Features.Strategies.SetTradeStrategy;

// ============================================================================
//  SetTradeStrategyCommand + Handler — slice 3a.
//
//  PUT /api/trades/{tradeId}/strategy  body: { strategyId: Guid | null }
//
//  Cross-user scope (defense in depth):
//   - Trade debe pertenecer al UserId autenticado. Else NotFound.
//   - Strategy (si no-null) debe pertenecer al MISMO UserId. Else NotFound.
//     Esto NO leak existencia: la response es indistinguible para missing
//     vs foreign-ownership.
//   - La strategy debe estar activa. Soft-deleted NO se puede asignar.
//     Else NotFound (mismo prefijo).
//
//  El handler delega la mutacion al aggregate Trade.SetStrategyId, que
//  enforce la invariante "Cancelled es inmutable" a nivel dominio.
// ============================================================================

public sealed record SetTradeStrategyCommand(
    Guid TradeId,
    Guid UserId,
    Guid? StrategyId) : IRequest<Result<StrategyDto>>;

public sealed class SetTradeStrategyValidator : AbstractValidator<SetTradeStrategyCommand>
{
    public SetTradeStrategyValidator()
    {
        RuleFor(x => x.TradeId).NotEqual(Guid.Empty);
        RuleFor(x => x.UserId).NotEqual(Guid.Empty);
        RuleFor(x => x.StrategyId)
            .Must(s => !s.HasValue || s.Value != Guid.Empty)
            .WithMessage("StrategyId must be either null or a non-empty Guid.");
    }
}

public sealed class SetTradeStrategyHandler
    : IRequestHandler<SetTradeStrategyCommand, Result<StrategyDto>>
{
    private readonly ITradeRepository _trades;
    private readonly IStrategyRepository _strategies;
    private readonly IUnitOfWork _uow;

    public SetTradeStrategyHandler(
        ITradeRepository trades,
        IStrategyRepository strategies,
        IUnitOfWork uow)
    {
        _trades = trades;
        _strategies = strategies;
        _uow = uow;
    }

    public async Task<Result<StrategyDto>> Handle(
        SetTradeStrategyCommand req,
        CancellationToken ct)
    {
        // Trade must belong to the user.
        var trade = await _trades.FindByIdAsync(req.TradeId, ct);
        if (trade is null || trade.UserId != req.UserId)
            return Result.Failure<StrategyDto>(
                TradingApplicationErrors.Strategies.TradeNotFound);

        // Si vamos a untag, no necesitamos validar strategy existence.
        // Si vamos a tag, la strategy debe pertenecer al user Y estar activa.
        if (req.StrategyId.HasValue)
        {
            var strategy = await _strategies.GetByIdAsync(req.StrategyId.Value, ct);
            if (strategy is null
                || strategy.UserId != req.UserId
                || !strategy.IsActive)
            {
                return Result.Failure<StrategyDto>(
                    TradingApplicationErrors.Strategies.NotFound);
            }
        }

        var setResult = trade.SetStrategyId(req.StrategyId);
        if (setResult.IsFailure)
            return Result.Failure<StrategyDto>(setResult.Error);

        await _trades.UpdateAsync(trade, ct);

        var saved = await _uow.SaveChangesAsync(ct);
        if (saved.IsFailure)
            return Result.Failure<StrategyDto>(saved.Error);

        // Devolvemos la strategy (post-tag) o null (post-untag).
        if (req.StrategyId.HasValue)
        {
            var strategy = await _strategies.GetByIdAsync(req.StrategyId.Value, ct);
            return Result.Success(strategy!.ToDto());
        }

        // Untag: devolvemos un DTO sentinel para mantener el shape de la respuesta.
        // El FE ignora este caso (uso tipico: devuelve el trade para refrescar la UI).
        return Result.Success(new StrategyDto(
            Guid.Empty, req.UserId, string.Empty, null, null, null, null,
            false, trade.UpdatedAt ?? trade.CreatedAt, trade.UpdatedAt ?? trade.CreatedAt));
    }
}