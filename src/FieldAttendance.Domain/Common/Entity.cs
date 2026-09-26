namespace FieldAttendance.Domain.Common;

/// <summary>
/// Base for all persisted entities. Nothing is ever hard-deleted:
/// "delete" in the UI maps to <see cref="Deactivate"/> so history and reports stay intact.
/// Audit fields and <see cref="RowVersion"/> are maintained by the persistence layer.
/// </summary>
public abstract class Entity
{
    public Guid Id { get; protected init; } = Guid.NewGuid();
    public bool IsActive { get; private set; } = true;

    public DateTimeOffset CreatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
    public Guid? UpdatedBy { get; set; }

    /// <summary>Optimistic concurrency token (SQL Server rowversion). Required for autosave conflict detection.</summary>
    public byte[] RowVersion { get; set; } = [];

    public void Deactivate() => IsActive = false;
    public void Activate() => IsActive = true;
}
