namespace JadeCapital.Shared.Kernel.Primitives;

/// <summary>
/// Base para todas las entidades de dominio. Identidad = TId. Igualdad por Id y tipo.
/// CreatedAt / UpdatedAt para auditoria. TId : notnull permite Guid, Ulid, long, etc.
/// </summary>
public abstract class Entity<TId>
    where TId : notnull
{
    public TId Id { get; protected set; } = default!;
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? UpdatedAt { get; private set; }

    protected Entity() { }

    protected Entity(TId id)
    {
        if (id is null) throw new ArgumentNullException(nameof(id));
        Id = id;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>Marca la entidad como modificada. Llamar desde setters que cambian estado.</summary>
    protected void Touch()
    {
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public override bool Equals(object? obj)
    {
        if (obj is not Entity<TId> other) return false;
        if (ReferenceEquals(this, other)) return true;
        if (GetType() != other.GetType()) return false; // tipo EXACTO, no jerarquia
        return EqualityComparer<TId>.Default.Equals(Id, other.Id);
    }

    public override int GetHashCode()
        => HashCode.Combine(GetType(), Id);

    public static bool operator ==(Entity<TId>? a, Entity<TId>? b)
        => a is null ? b is null : a.Equals(b);

    public static bool operator !=(Entity<TId>? a, Entity<TId>? b) => !(a == b);
}