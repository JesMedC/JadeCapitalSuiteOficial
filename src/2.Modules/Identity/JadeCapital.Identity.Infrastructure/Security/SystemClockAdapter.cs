using JadeCapital.Identity.Application.Abstractions;

namespace JadeCapital.Identity.Infrastructure.Security;

/// <summary>
/// Implementacion de <see cref="IClock"/> del modulo Identity.
/// Reusa <see cref="JadeCapital.Shared.Kernel.Time.SystemClock"/> via composicion
/// para evitar duplicar logica de tiempo.
/// </summary>
public sealed class SystemClockAdapter : IClock
{
    private readonly JadeCapital.Shared.Kernel.Time.IClock _inner;
    public SystemClockAdapter(JadeCapital.Shared.Kernel.Time.IClock inner) => _inner = inner;
    public DateTimeOffset UtcNow => _inner.UtcNow;
}