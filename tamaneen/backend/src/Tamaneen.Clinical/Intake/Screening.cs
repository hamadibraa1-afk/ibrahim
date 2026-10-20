using Tamaneen.SharedKernel;

namespace Tamaneen.Clinical.Intake;

/// <summary>The short C-SSRS safety check (P23). Answers are encrypted; only the result is plain.</summary>
public sealed class Screening : Entity
{
    private Screening(Guid id, Guid patientId, string instrument, string version, bool positive, RiskLevel level, byte[] answersCipher, DateTimeOffset now)
        : base(id)
    {
        PatientId = patientId;
        Instrument = instrument;
        Version = version;
        Positive = positive;
        Level = level;
        AnswersCipher = answersCipher;
        CompletedAtUtc = now;
    }

    // EF Core
    private Screening()
    {
        Instrument = string.Empty;
        Version = string.Empty;
        AnswersCipher = Array.Empty<byte>();
    }

    public Guid PatientId { get; private set; }

    public string Instrument { get; private set; }

    public string Version { get; private set; }

    public bool Positive { get; private set; }

    public RiskLevel Level { get; private set; }

    public byte[] AnswersCipher { get; private set; }

    public DateTimeOffset CompletedAtUtc { get; private set; }

    public static Screening Record(Guid patientId, string instrument, string version, bool positive, RiskLevel level, byte[] answersCipher, IClock clock) =>
        new(Guid.NewGuid(), patientId, instrument, version, positive, level, answersCipher, clock.UtcNow);
}
