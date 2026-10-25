using Tamaneen.SharedKernel;

namespace Tamaneen.Clinical.Sessions;

/// <summary>
/// P41 «عن ماذا تحب أن نتحدث اليوم؟»: what the patient would like to talk about, written in the lobby and read by the
/// therapist before the session. Clinical free text: encrypted with the patient's key and passed through the crisis gate.
/// </summary>
public sealed class SessionPrep
{
    public const int MaxLength = 1000;

    private SessionPrep(Guid appointmentId, Guid patientId, Guid therapistUserId, byte[] noteCipher, DateTimeOffset now)
    {
        AppointmentId = appointmentId;
        PatientId = patientId;
        TherapistUserId = therapistUserId;
        NoteCipher = noteCipher;
        UpdatedAtUtc = now;
    }

    // EF Core
    private SessionPrep()
    {
        NoteCipher = [];
    }

    public Guid AppointmentId { get; private set; }

    public Guid PatientId { get; private set; }

    public Guid TherapistUserId { get; private set; }

    public byte[] NoteCipher { get; private set; }

    public DateTimeOffset UpdatedAtUtc { get; private set; }

    public static SessionPrep Write(Guid appointmentId, Guid patientId, Guid therapistUserId, byte[] noteCipher, IClock clock) =>
        new(appointmentId, patientId, therapistUserId, noteCipher, clock.UtcNow);

    public void Rewrite(byte[] noteCipher, IClock clock)
    {
        NoteCipher = noteCipher;
        UpdatedAtUtc = clock.UtcNow;
    }
}
