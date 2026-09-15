namespace Mise.SharedKernel;

/// <summary>
/// Base type for an entity identified by <typeparamref name="TId"/>. Equality is by
/// identity (same concrete type, same Id) — never by value.
/// </summary>
public abstract class Entity<TId>(TId id) : IEquatable<Entity<TId>>
    where TId : notnull
{
    public TId Id { get; } = id;

    public bool Equals(Entity<TId>? other) =>
        other is not null && (ReferenceEquals(this, other) || (GetType() == other.GetType() && Id.Equals(other.Id)));

    public override bool Equals(object? obj) => Equals(obj as Entity<TId>);

    public override int GetHashCode() => HashCode.Combine(GetType(), Id);

    public static bool operator ==(Entity<TId>? left, Entity<TId>? right) =>
        left is null ? right is null : left.Equals(right);

    public static bool operator !=(Entity<TId>? left, Entity<TId>? right) => !(left == right);
}
