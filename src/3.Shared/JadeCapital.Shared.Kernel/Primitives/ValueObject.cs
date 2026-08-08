namespace JadeCapital.Shared.Kernel.Primitives;

/// <summary>
/// Base para Value Objects. Inmutable. Igualdad por valor (todos los componentes).
/// Override GetEqualityComponents() para devolver las propiedades que participan en la igualdad.
/// </summary>
public abstract class ValueObject : IEquatable<ValueObject>
{
    protected abstract IEnumerable<object?> GetEqualityComponents();

    public bool Equals(ValueObject? other)
    {
        if (other is null) return false;
        if (GetType() != other.GetType()) return false;
        return GetEqualityComponents().SequenceEqual(other.GetEqualityComponents());
    }

    public override bool Equals(object? obj) => obj is ValueObject v && Equals(v);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        foreach (var c in GetEqualityComponents()) hash.Add(c);
        return hash.ToHashCode();
    }

    public static bool operator ==(ValueObject? a, ValueObject? b)
        => a is null ? b is null : a.Equals(b);

    public static bool operator !=(ValueObject? a, ValueObject? b) => !(a == b);
}