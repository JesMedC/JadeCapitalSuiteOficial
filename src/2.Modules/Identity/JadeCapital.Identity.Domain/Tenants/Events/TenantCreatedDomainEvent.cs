using JadeCapital.Shared.Kernel.Primitives;

namespace JadeCapital.Identity.Domain.Tenants.Events;

/// <summary>
/// Domain event raised when a <see cref="Tenant"/> is created (Wave 6,
/// slice 6c.1).
///
/// <para>
/// Dispatched by <see cref="Tenant.Create"/>; consumed in slice 6c.2 by the
/// <c>BackfillTenantsRunner</c> to wire the creator's <c>tenant_id</c>
/// column to the new aggregate.
/// </para>
/// </summary>
public sealed record TenantCreatedDomainEvent(
    Guid TenantId,
    Guid OwnerUserId,
    string Slug,
    DateTimeOffset OccurredOn) : IDomainEvent;
