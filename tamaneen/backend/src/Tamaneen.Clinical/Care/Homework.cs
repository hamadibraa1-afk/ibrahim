using Tamaneen.SharedKernel;

namespace Tamaneen.Clinical.Care;

public enum HomeworkFrequency
{
    Daily = 0,
    TwiceWeekly = 1,
    Weekly = 2,
    Once = 3,
}

public enum CheckinStatus
{
    Done = 0,
    Partial = 1,
    Skipped = 2,
}

/// <summary>An exercise a therapist assigns (P10, P29). Title and instructions are encrypted clinical content.</summary>
public sealed class HomeworkAssignment : Entity
{
    private HomeworkAssignment(Guid id, Guid patientId, Guid therapistUserId, byte[] titleCipher, byte[] instructionsCipher, int? minutes, HomeworkFrequency frequency, DateOnly startsOn, DateOnly endsOn, DateTimeOffset now)
        : base(id)
    {
        PatientId = patientId;
        TherapistUserId = therapistUserId;
        TitleCipher = titleCipher;
        InstructionsCipher = instructionsCipher;
        Minutes = minutes;
        Frequency = frequency;
        StartsOn = startsOn;
        EndsOn = endsOn;
        CreatedAtUtc = now;
    }

    // EF Core
    private HomeworkAssignment()
    {
        TitleCipher = Array.Empty<byte>();
        InstructionsCipher = Array.Empty<byte>();
    }

    public Guid PatientId { get; private set; }

    public Guid TherapistUserId { get; private set; }

    public byte[] TitleCipher { get; private set; }

    /// <summary>Encrypted JSON array of instruction lines.</summary>
    public byte[] InstructionsCipher { get; private set; }

    public int? Minutes { get; private set; }

    public HomeworkFrequency Frequency { get; private set; }

    public DateOnly StartsOn { get; private set; }

    public DateOnly EndsOn { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }

    /// <summary>Library template it came from (S16), or null for the therapist's own text.</summary>
    public string? TemplateCode { get; private set; }

    public DateTimeOffset? UpdatedAtUtc { get; private set; }

    /// <summary>Set when the therapist ends the exercise early (S16 "cancel"); the patient then sees it as finished.</summary>
    public DateTimeOffset? CancelledAtUtc { get; private set; }

    public bool IsCancelled => CancelledAtUtc is not null;

    public static HomeworkAssignment Assign(Guid patientId, Guid therapistUserId, byte[] titleCipher, byte[] instructionsCipher, int? minutes, HomeworkFrequency frequency, DateOnly startsOn, DateOnly endsOn, IClock clock)
    {
        if (endsOn < startsOn)
        {
            throw new DomainException(ErrorCodes.Validation, "Homework must end on or after its start.");
        }

        return new HomeworkAssignment(Guid.NewGuid(), patientId, therapistUserId, titleCipher, instructionsCipher, minutes, frequency, startsOn, endsOn, clock.UtcNow);
    }

    public void SetTemplate(string? templateCode) => TemplateCode = templateCode;

    /// <summary>S16 edit: the patient sees the change at once. An ended exercise cannot be edited.</summary>
    public void Update(byte[] titleCipher, byte[] instructionsCipher, int? minutes, HomeworkFrequency frequency, DateOnly endsOn, DateOnly today, IClock clock)
    {
        if (IsCompleted(today))
        {
            throw new DomainException(ErrorCodes.HomeworkEnded, $"Homework {Id} has ended.");
        }

        if (endsOn < StartsOn || endsOn < today)
        {
            throw new DomainException(ErrorCodes.Validation, "Homework must end on or after its start and today.");
        }

        TitleCipher = titleCipher;
        InstructionsCipher = instructionsCipher;
        Minutes = minutes;
        Frequency = frequency;
        EndsOn = endsOn;
        UpdatedAtUtc = clock.UtcNow;
    }

    public void Cancel(DateOnly today, IClock clock)
    {
        if (IsCompleted(today))
        {
            throw new DomainException(ErrorCodes.HomeworkEnded, $"Homework {Id} has ended.");
        }

        CancelledAtUtc = clock.UtcNow;
        UpdatedAtUtc = clock.UtcNow;
    }

    public bool IsCompleted(DateOnly today) => today > EndsOn || CancelledAtUtc is not null;

    /// <summary>Days in the assignment for daily work (P10 "3 of 7 days"); target count for the other frequencies.</summary>
    public int TotalDays => Frequency switch
    {
        HomeworkFrequency.Daily => EndsOn.DayNumber - StartsOn.DayNumber + 1,
        HomeworkFrequency.TwiceWeekly => Math.Max(1, (int)Math.Ceiling((EndsOn.DayNumber - StartsOn.DayNumber + 1) / 7.0 * 2)),
        HomeworkFrequency.Weekly => Math.Max(1, (int)Math.Ceiling((EndsOn.DayNumber - StartsOn.DayNumber + 1) / 7.0)),
        _ => 1,
    };

    /// <summary>Whether today is a day the exercise is expected (daily, or not yet done this week for the others).</summary>
    public bool DueOn(DateOnly day, int doneThisWeek) =>
        day >= StartsOn && day <= EndsOn && Frequency switch
        {
            HomeworkFrequency.Daily => true,
            HomeworkFrequency.TwiceWeekly => doneThisWeek < 2,
            HomeworkFrequency.Weekly => doneThisWeek < 1,
            _ => doneThisWeek < 1,
        };
}

/// <summary>A patient's report for one day of an assignment, plus the therapist's reply.</summary>
public sealed class HomeworkCheckin : Entity
{
    private HomeworkCheckin(Guid id, Guid assignmentId, Guid patientId, DateOnly day, DateTimeOffset now)
        : base(id)
    {
        AssignmentId = assignmentId;
        PatientId = patientId;
        Day = day;
        CreatedAtUtc = now;
    }

    // EF Core
    private HomeworkCheckin()
    {
    }

    public Guid AssignmentId { get; private set; }

    public Guid PatientId { get; private set; }

    public DateOnly Day { get; private set; }

    public CheckinStatus Status { get; private set; }

    public int? Feeling { get; private set; }

    public byte[]? NoteCipher { get; private set; }

    public byte[]? FeedbackCipher { get; private set; }

    public DateTimeOffset? FeedbackAtUtc { get; private set; }

    public DateTimeOffset? FeedbackSeenAtUtc { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public DateTimeOffset UpdatedAtUtc { get; private set; }

    /// <summary>The crisis gate's level for the note (S16 shows a risk badge at 3 or 4); null without a note.</summary>
    public RiskLevel? GateLevel { get; private set; }

    public void SetGateLevel(RiskLevel? level) => GateLevel = level;

    public static HomeworkCheckin Start(Guid assignmentId, Guid patientId, DateOnly day, IClock clock) => new(Guid.NewGuid(), assignmentId, patientId, day, clock.UtcNow);

    public void Report(CheckinStatus status, int? feeling, byte[]? noteCipher, IClock clock)
    {
        if (feeling is < 0 or > 10)
        {
            throw new DomainException(ErrorCodes.Validation, "Feeling must be between 0 and 10.");
        }

        Status = status;
        Feeling = feeling;
        NoteCipher = noteCipher;
        UpdatedAtUtc = clock.UtcNow;
    }

    public void GiveFeedback(byte[] cipher, IClock clock)
    {
        FeedbackCipher = cipher;
        FeedbackAtUtc = clock.UtcNow;
        FeedbackSeenAtUtc = null;
    }

    public void MarkFeedbackSeen(IClock clock) => FeedbackSeenAtUtc ??= clock.UtcNow;
}
