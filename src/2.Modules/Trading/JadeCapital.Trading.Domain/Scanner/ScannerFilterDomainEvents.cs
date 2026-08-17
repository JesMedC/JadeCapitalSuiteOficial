using JadeCapital.Shared.Kernel.Primitives;

namespace JadeCapital.Trading.Domain.Scanner;

// ============================================================================
//  ScannerFilter domain events.
// ============================================================================

public sealed record ScannerFilterCreatedDomainEvent(Guid FilterId, Guid UserId, DateTimeOffset At) : IDomainEvent
{
    public DateTimeOffset OccurredOn { get; } = At;
}

public sealed record ScannerFilterUpdatedDomainEvent(Guid FilterId, Guid UserId, DateTimeOffset At) : IDomainEvent
{
    public DateTimeOffset OccurredOn { get; } = At;
}
