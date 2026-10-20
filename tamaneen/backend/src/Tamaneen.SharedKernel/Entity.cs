namespace Tamaneen.SharedKernel;

/// <summary>Base type for entities identified by a GUID. Equality is by identity, not by state.</summary>
public abstract class Entity
{
    protected Entity(Guid id)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("Entity id must not be empty.", nameof(id));
        }

        Id = id;
    }

    // Required by EF Core materialisation.
    protected Entity()
    {
    }

    public Guid Id { get; private set; }

    public override bool Equals(object? obj) =>
        obj is Entity other && other.GetType() == GetType() && other.Id == Id;

    public override int GetHashCode() => HashCode.Combine(GetType(), Id);
}
