namespace JadeCapital.Shared.Kernel.Primitives;

/// <summary>
/// Contrato de evento de dominio. Disparado por agregados, despachado por Infrastructure.
/// Inmutable. Identidad = tipo + payload + timestamp.
/// </summary>
public interface IDomainEvent
{
    DateTimeOffset OccurredOn { get; }
}