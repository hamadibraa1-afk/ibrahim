using Tamaneen.SharedKernel;

namespace Tamaneen.Clinical.Care;

/// <summary>
/// The Stanley–Brown style safety plan (P37), written by the patient with their therapist. All sections
/// are one encrypted JSON value.
/// </summary>
public sealed class SafetyPlan
{
    public static readonly IReadOnlyList<string> Sections = ["warningSigns", "copingAlone", "distractions", "peopleToAsk", "professionals", "safeEnvironment", "reasonToLive"];

    private SafetyPlan(Guid patientId) => PatientId = patientId;

    // EF Core
    private SafetyPlan()
    {
        SectionsCipher = Array.Empty<byte>();
    }

    public Guid PatientId { get; private set; }

    public byte[] SectionsCipher { get; private set; } = Array.Empty<byte>();

    public DateTimeOffset UpdatedAtUtc { get; private set; }

    public Guid UpdatedByUserId { get; private set; }

    public string UpdatedByRole { get; private set; } = string.Empty;

    /// <summary>The therapist who last worked on it with the patient.</summary>
    public Guid? CoAuthorTherapistId { get; private set; }

    /// <summary>1 after the first save, +1 on every save by the patient (P37) or the therapist (S35). The ETag of the plan.</summary>
    public int Version { get; private set; }

    public static SafetyPlan Create(Guid patientId) => new(patientId);

    public void Update(byte[] sectionsCipher, Guid userId, string role, IClock clock)
    {
        SectionsCipher = sectionsCipher;
        UpdatedAtUtc = clock.UtcNow;
        UpdatedByUserId = userId;
        UpdatedByRole = role;
        Version++;
        if (role == AppRoles.Therapist)
        {
            CoAuthorTherapistId = userId;
        }
    }
}

/// <summary>A saved version of a safety plan; every save adds one and none is ever changed (S35 "previous versions").</summary>
public sealed class SafetyPlanVersion : Entity
{
    private SafetyPlanVersion(Guid id, Guid patientId, int version, byte[] sectionsCipher, Guid authorUserId, string authorRole, DateTimeOffset now)
        : base(id)
    {
        PatientId = patientId;
        Version = version;
        SectionsCipher = sectionsCipher;
        AuthorUserId = authorUserId;
        AuthorRole = authorRole;
        CreatedAtUtc = now;
    }

    // EF Core
    private SafetyPlanVersion()
    {
        SectionsCipher = Array.Empty<byte>();
        AuthorRole = string.Empty;
    }

    public Guid PatientId { get; private set; }

    public int Version { get; private set; }

    public byte[] SectionsCipher { get; private set; }

    public Guid AuthorUserId { get; private set; }

    public string AuthorRole { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public static SafetyPlanVersion Of(SafetyPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        return new SafetyPlanVersion(Guid.NewGuid(), plan.PatientId, plan.Version, plan.SectionsCipher, plan.UpdatedByUserId, plan.UpdatedByRole, plan.UpdatedAtUtc);
    }
}
