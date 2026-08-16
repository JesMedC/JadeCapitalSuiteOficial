using JadeCapital.Shared.Kernel.Primitives;

namespace JadeCapital.Trading.Domain.Planner;

// ============================================================================
//  PlannerSession domain events — slice 3c.
//
//  Cada evento lleva el SessionId y el UserId para que un dispatcher
//  futuro (Wave 4+) pueda enrutar al bus sin re-cargar el aggregate.
//  `OccurredOn` viene del clock inyectado en el momento del raise.
// ============================================================================

public sealed record PlannerSessionCreatedDomainEvent(
    Guid SessionId,
    Guid UserId,
    DateTimeOffset OccurredOn) : IDomainEvent;