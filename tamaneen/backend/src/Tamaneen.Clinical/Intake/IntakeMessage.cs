using Tamaneen.SharedKernel;

namespace Tamaneen.Clinical.Intake;

/// <summary>One turn of the intake conversation. The text is encrypted with the patient's data key.</summary>
public sealed class IntakeMessage : Entity
{
    private IntakeMessage(Guid id, Guid intakeId, Guid patientId, bool fromPatient, byte[] textCipher, bool isFallback, DateTimeOffset now)
        : base(id)
    {
        IntakeId = intakeId;
        PatientId = patientId;
        FromPatient = fromPatient;
        TextCipher = textCipher;
        IsFallback = isFallback;
        CreatedAtUtc = now;
    }

    // EF Core
    private IntakeMessage()
    {
        TextCipher = Array.Empty<byte>();
    }

    public Guid IntakeId { get; private set; }

    public Guid PatientId { get; private set; }

    public bool FromPatient { get; private set; }

    public byte[] TextCipher { get; private set; }

    /// <summary>True when the assistant's turn is the fixed fallback (model unavailable or reply rejected).</summary>
    public bool IsFallback { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }

    /// <summary>Id the app gave the patient's message (PB-21); a retry with the same id is not stored twice (EC-07).</summary>
    public Guid? ClientMessageId { get; private set; }

    public static IntakeMessage Patient(Guid intakeId, Guid patientId, byte[] cipher, IClock clock, Guid? clientMessageId = null) =>
        new(Guid.NewGuid(), intakeId, patientId, true, cipher, false, clock.UtcNow) { ClientMessageId = clientMessageId };

    public static IntakeMessage Assistant(Guid intakeId, Guid patientId, byte[] cipher, bool isFallback, IClock clock) => new(Guid.NewGuid(), intakeId, patientId, false, cipher, isFallback, clock.UtcNow);
}
