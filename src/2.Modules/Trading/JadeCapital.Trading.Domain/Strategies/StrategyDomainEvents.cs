using JadeCapital.Shared.Kernel.Primitives;

namespace JadeCapital.Trading.Domain.Strategies;

// ============================================================================
//  Strategy domain events — slice 3a (Trader Strategies + Alerts + Planner).
//
//  Cada evento lleva el StrategyId y el UserId para que un dispatcher
//  futuro (Wave 4+) pueda enrutar al bus sin re-cargar el aggregate.
//  `OccurredOn` viene del clock inyectado en el momento del raise — es
//  la verdad de cuando el evento se emitio, no de cuando el handler
//  lo persistio.
// ============================================================================

public sealed record StrategyCreatedDomainEvent(
    Guid StrategyId,
    Guid UserId,
    DateTimeOffset OccurredOn) : IDomainEvent;

public sealed record StrategyUpdatedDomainEvent(
    Guid StrategyId,
    Guid UserId,
    DateTimeOffset OccurredOn) : IDomainEvent;

public sealed record StrategySoftDeletedDomainEvent(
    Guid StrategyId,
    Guid UserId,
    DateTimeOffset OccurredOn) : IDomainEvent;