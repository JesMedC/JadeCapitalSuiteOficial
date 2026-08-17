using JadeCapital.Shared.Kernel.Primitives;

namespace JadeCapital.Identity.Domain.RiskProfile;

/// <summary>
/// Emitido por <see cref="RiskProfile.Create"/> en su factory. Marca que un
/// nuevo perfil de riesgo ha sido creado para el usuario dado. Subscribers
/// (auditoria, alerts, etc.) pueden leer <see cref="RiskProfileId"/> para
/// referenciar el agregado recien persistido.
/// </summary>
public sealed record RiskProfileCreatedDomainEvent(
    Guid RiskProfileId,
    Guid UserId,
    DateTimeOffset OccurredOn) : IDomainEvent;

/// <summary>
/// Emitido cuando un perfil activo es supersedado (transiciona a isActive=FALSE).
/// El consumidor puede usar <see cref="RiskProfileId"/> para asociar el evento
/// al agregado que deja de estar activo.
/// </summary>
public sealed record RiskProfileSupersededDomainEvent(
    Guid RiskProfileId,
    Guid UserId,
    DateTimeOffset OccurredOn) : IDomainEvent;

/// <summary>
/// Emitido por <see cref="RiskProfile.Update"/> cuando los valores de un perfil
/// activo cambian sin supersede (e.g. una mutacion en sitio). Como una fila
/// activa es la unica que existe a la vez, este evento representa un cambio
/// de valores sobre el mismo agregado.
/// </summary>
public sealed record RiskProfileUpdatedDomainEvent(
    Guid RiskProfileId,
    Guid UserId,
    DateTimeOffset OccurredOn) : IDomainEvent;
