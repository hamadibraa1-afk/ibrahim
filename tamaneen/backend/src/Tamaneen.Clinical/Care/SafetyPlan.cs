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

    public static SafetyPlan Create(Guid patientId) => new(patientId);

    public void Update(byte[] sectionsCipher, Guid userId, string role, IClock clock)
    {
        SectionsCipher = sectionsCipher;
        UpdatedAtUtc = clock.UtcNow;
        UpdatedByUserId = userId;
        UpdatedByRole = role;
        if (role == AppRoles.Therapist)
        {
            CoAuthorTherapistId = userId;
        }
    }
}
