using Tamaneen.SharedKernel;

namespace Tamaneen.Clinical.Care;

/// <summary>"I want to talk to a person" (P04, P22): the care team calls back.</summary>
public sealed class CareRequest : Entity
{
    private CareRequest(Guid id, Guid patientId, string source, DateTimeOffset now)
        : base(id)
    {
        PatientId = patientId;
        Source = source;
        RequestedAtUtc = now;
    }

    // EF Core
    private CareRequest()
    {
        Source = string.Empty;
    }

    public Guid PatientId { get; private set; }

    public string Source { get; private set; }

    public DateTimeOffset RequestedAtUtc { get; private set; }

    public DateTimeOffset? HandledAtUtc { get; private set; }

    public Guid? HandledBy { get; private set; }

    public static CareRequest Create(Guid patientId, string source, IClock clock) => new(Guid.NewGuid(), patientId, source, clock.UtcNow);

    public void Handle(Guid staffId, IClock clock)
    {
        HandledAtUtc ??= clock.UtcNow;
        HandledBy ??= staffId;
    }
}

public enum DataRequestKind
{
    Export = 0,
    Erasure = 1,
}

/// <summary>A data-subject request (P11): a copy of my data within 7 days, or account deletion.</summary>
public sealed class DataRequest : Entity
{
    public static readonly TimeSpan ExportWithin = TimeSpan.FromDays(7);

    private DataRequest(Guid id, Guid patientId, DataRequestKind kind, DateTimeOffset now)
        : base(id)
    {
        PatientId = patientId;
        Kind = kind;
        RequestedAtUtc = now;
        DueAtUtc = now + ExportWithin;
    }

    // EF Core
    private DataRequest()
    {
    }

    public Guid PatientId { get; private set; }

    public DataRequestKind Kind { get; private set; }

    public DateTimeOffset RequestedAtUtc { get; private set; }

    public DateTimeOffset DueAtUtc { get; private set; }

    public DateTimeOffset? CompletedAtUtc { get; private set; }

    public static DataRequest Create(Guid patientId, DataRequestKind kind, IClock clock) => new(Guid.NewGuid(), patientId, kind, clock.UtcNow);

    public void Complete(IClock clock) => CompletedAtUtc ??= clock.UtcNow;
}
