using Tamaneen.SharedKernel;

namespace Tamaneen.Clinical.Care;

public enum SessionFrequency
{
    Weekly = 0,
    Biweekly = 1,
    Monthly = 2,
}

/// <summary>
/// One version of a patient's treatment plan (S15). Every save adds a version; versions are never changed or deleted.
/// Goals, interventions and follow-up notes are one encrypted JSON value (patient key).
/// </summary>
public sealed class TreatmentPlanVersion : Entity
{
    public const int MaxTextLength = 4000;

    private TreatmentPlanVersion(Guid id, Guid patientId, Guid therapistUserId, int version, byte[] contentCipher, SessionFrequency frequency, DateOnly reviewOn, DateTimeOffset now)
        : base(id)
    {
        PatientId = patientId;
        TherapistUserId = therapistUserId;
        Version = version;
        ContentCipher = contentCipher;
        Frequency = frequency;
        ReviewOn = reviewOn;
        CreatedAtUtc = now;
    }

    // EF Core
    private TreatmentPlanVersion()
    {
        ContentCipher = Array.Empty<byte>();
    }

    public Guid PatientId { get; private set; }

    public Guid TherapistUserId { get; private set; }

    /// <summary>1, 2, 3… per patient.</summary>
    public int Version { get; private set; }

    public byte[] ContentCipher { get; private set; }

    public SessionFrequency Frequency { get; private set; }

    public DateOnly ReviewOn { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }

    /// <summary>The next version after <paramref name="currentVersion"/>; the review date must be after <paramref name="today"/>.</summary>
    public static TreatmentPlanVersion Create(Guid patientId, Guid therapistUserId, int currentVersion, byte[] contentCipher, SessionFrequency frequency, DateOnly reviewOn, DateOnly today, IClock clock)
    {
        if (reviewOn <= today)
        {
            throw new DomainException(ErrorCodes.Validation, "The review date must be in the future.");
        }

        return new TreatmentPlanVersion(Guid.NewGuid(), patientId, therapistUserId, currentVersion + 1, contentCipher, frequency, reviewOn, clock.UtcNow);
    }
}
