using Tamaneen.SharedKernel;

namespace Tamaneen.Clinical.Care;

/// <summary>A mood check-in, 0 (very bad) to 10 (excellent), with optional tags and an encrypted note.</summary>
public sealed class MoodEntry : Entity
{
    public static readonly IReadOnlySet<string> TagCodes = new HashSet<string>(StringComparer.Ordinal) { "tired", "anxious", "slept_well", "work_stress", "calm" };

    private MoodEntry(Guid id, Guid patientId, int score, string tags, byte[]? noteCipher, DateTimeOffset now)
        : base(id)
    {
        PatientId = patientId;
        Score = score;
        Tags = tags;
        NoteCipher = noteCipher;
        RecordedAtUtc = now;
    }

    // EF Core
    private MoodEntry()
    {
        Tags = string.Empty;
    }

    public Guid PatientId { get; private set; }

    public int Score { get; private set; }

    public string Tags { get; private set; }

    public byte[]? NoteCipher { get; private set; }

    public DateTimeOffset RecordedAtUtc { get; private set; }

    public IReadOnlyList<string> TagList => Tags.Split(',', StringSplitOptions.RemoveEmptyEntries);

    public static MoodEntry Record(Guid patientId, int score, IEnumerable<string> tags, byte[]? noteCipher, IClock clock)
    {
        if (score is < 0 or > 10)
        {
            throw new DomainException(ErrorCodes.Validation, "Mood score must be between 0 and 10.");
        }

        var list = tags.Distinct().ToList();
        if (list.Any(t => !TagCodes.Contains(t)))
        {
            throw new DomainException(ErrorCodes.Validation, "Unknown mood tag.");
        }

        return new MoodEntry(Guid.NewGuid(), patientId, score, string.Join(',', list), noteCipher, clock.UtcNow);
    }
}
