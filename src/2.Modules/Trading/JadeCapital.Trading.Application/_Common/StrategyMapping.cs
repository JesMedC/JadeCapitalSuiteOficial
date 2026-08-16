using JadeCapital.Trading.Contracts.Strategies;
using JadeCapital.Trading.Domain.Strategies;

namespace JadeCapital.Trading.Application._Common;

// ============================================================================
//  StrategyMappingExtensions — slice 3a.
//
//  Extension centralizada para proyectar el aggregate Strategy al DTO wire.
//  Vive en Application porque es contrato de capa: handlers, queries y
//  (futuros) projections de EF lo consumen.
//
//  Timeframe se proyecta como byte? (subyacente del enum). El FE mapea
//  byte → label (M1..MN) via la constante TIMEFRAME_LABELS.
// ============================================================================

public static class StrategyMappingExtensions
{
    public static StrategyDto ToDto(this Strategy s)
        => new(
            s.Id,
            s.UserId,
            s.Name,
            s.Description,
            s.Symbol,
            s.Timeframe.HasValue ? (byte)s.Timeframe.Value : (byte?)null,
            s.Rules,
            s.IsActive,
            s.CreatedAt,
            s.UpdatedAt ?? s.CreatedAt);
}