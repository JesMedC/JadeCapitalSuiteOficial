using JadeCapital.Trading.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace JadeCapital.Trading.Infrastructure.Persistence.Converters;

/// <summary>
/// ValueConverter Symbol &lt;-&gt; string. Persiste <see cref="Symbol.Value"/>
/// como VARCHAR(20) en la columna "symbol". El path inverso usa
/// <see cref="Symbol.FromTrusted"/> porque el valor del DB ya pasó validación
/// al escribirse.
/// </summary>
public sealed class SymbolConverter : ValueConverter<Symbol, string>
{
    public SymbolConverter()
        : base(s => s.Value, v => Symbol.FromTrusted(v)) { }
}

/// <summary>
/// ValueComparer para detectar cambios en Symbol (value object inmutable).
/// Compara por Value — suficiente porque Symbol es inmutable.
/// </summary>
public sealed class SymbolComparer : ValueComparer<Symbol>
{
    public SymbolComparer()
        : base(
            (a, b) => a!.Value == b!.Value,
            v => v.Value.GetHashCode(),
            v => Symbol.FromTrusted(v.Value)) { }
}
