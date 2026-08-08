namespace JadeCapital.Shared.Kernel.Time;

/// <summary>
/// Abstraccion del reloj. SIEMPRE inyectar IClock en lugar de DateTimeOffset.UtcNow
/// para poder controlar el tiempo en tests.
/// </summary>
public interface IClock
{
    DateTimeOffset UtcNow { get; }
}

public sealed class SystemClock : IClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}