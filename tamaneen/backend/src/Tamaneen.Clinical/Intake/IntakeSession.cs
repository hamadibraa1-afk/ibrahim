using Tamaneen.SharedKernel;

namespace Tamaneen.Clinical.Intake;

public enum IntakeStatus
{
    Active = 0,
    Paused = 1,
    Review = 2,
    Submitted = 3,
    Expired = 4,
}

/// <summary>
/// The intake conversation (P04) and its summary (P05). After a few free-text turns the assistant asks
/// PHQ-9 then GAD-7, one item at a time; the patient may skip any item. Text lives in IntakeMessage rows,
/// encrypted; this row only holds scores and progress. Unfinished intakes can be resumed for 14 days.
/// </summary>
public sealed class IntakeSession : Entity
{
    public static readonly TimeSpan ResumeWindow = TimeSpan.FromDays(14);
    public const int TurnsBeforeQuestionnaires = 2;
    public const int MinTurnsToFinish = 3;

    private static readonly (string Instrument, int Items)[] Instruments = [("PHQ-9", 9), ("GAD-7", 7)];

    private IntakeSession(Guid id, Guid patientId, DateTimeOffset now)
        : base(id)
    {
        PatientId = patientId;
        StartedAtUtc = now;
        LastActivityAtUtc = now;
        Phq9 = new string('?', 9);
        Gad7 = new string('?', 7);
    }

    // EF Core
    private IntakeSession()
    {
        Phq9 = new string('?', 9);
        Gad7 = new string('?', 7);
    }

    public Guid PatientId { get; private set; }

    public IntakeStatus Status { get; private set; }

    public DateTimeOffset StartedAtUtc { get; private set; }

    public DateTimeOffset LastActivityAtUtc { get; private set; }

    public int PatientTurns { get; private set; }

    /// <summary>One character per item: '0'–'3' answered, '-' skipped, '?' not asked yet.</summary>
    public string Phq9 { get; private set; }

    public string Gad7 { get; private set; }

    /// <summary>Encrypted JSON of the summary sections and which ones the patient edited.</summary>
    public byte[]? SummaryCipher { get; private set; }

    public bool SummaryByAi { get; private set; }

    public DateTimeOffset? SubmittedAtUtc { get; private set; }

    /// <summary>
    /// The patient switched to the form (PB-28) because the assistant was unavailable: the questionnaires are
    /// asked straight away, the conversation turns are no longer needed to finish, and the patient writes the
    /// summary sections themselves.
    /// </summary>
    public bool FormMode { get; private set; }

    public DateTimeOffset? TherapistApprovedAtUtc { get; private set; }

    public Guid? TherapistApprovedBy { get; private set; }

    public DateTimeOffset ExpiresAtUtc => LastActivityAtUtc + ResumeWindow;

    public static IntakeSession Start(Guid patientId, IClock clock) => new(Guid.NewGuid(), patientId, clock.UtcNow);

    /// <summary>Marks the intake expired if the resume window passed. Returns true if it changed.</summary>
    public bool ExpireIfStale(IClock clock)
    {
        if (Status is IntakeStatus.Active or IntakeStatus.Paused && clock.UtcNow > ExpiresAtUtc)
        {
            Status = IntakeStatus.Expired;
            return true;
        }

        return false;
    }

    public void RecordPatientTurn(IClock clock)
    {
        EnsureConversationOpen(clock);
        PatientTurns++;
        Status = IntakeStatus.Active;
        LastActivityAtUtc = clock.UtcNow;
    }

    public void SwitchToForm(IClock clock)
    {
        EnsureConversationOpen(clock);
        FormMode = true;
        Status = IntakeStatus.Active;
        LastActivityAtUtc = clock.UtcNow;
    }

    public void Pause(IClock clock)
    {
        EnsureConversationOpen(clock);
        Status = IntakeStatus.Paused;
        LastActivityAtUtc = clock.UtcNow;
    }

    /// <summary>The next questionnaire item to ask, or null if none is due yet or all are done.</summary>
    public (string Instrument, int Item, int Total)? PendingQuestion()
    {
        if (Status is not (IntakeStatus.Active or IntakeStatus.Paused) || (PatientTurns < TurnsBeforeQuestionnaires && !FormMode))
        {
            return null;
        }

        foreach (var (instrument, items) in Instruments)
        {
            var index = Answers(instrument).IndexOf('?', StringComparison.Ordinal);
            if (index >= 0)
            {
                return (instrument, index + 1, items);
            }
        }

        return null;
    }

    /// <summary>Records an answer (0–3) or a skip (null) for the item that is currently pending.</summary>
    public void Answer(string instrument, int item, int? value, IClock clock)
    {
        EnsureConversationOpen(clock);
        var pending = PendingQuestion();
        if (pending is not { } p || p.Instrument != instrument || p.Item != item)
        {
            throw new DomainException(ErrorCodes.QuestionNotPending, $"{instrument} item {item} is not the pending question.");
        }

        if (value is < 0 or > 3)
        {
            throw new DomainException(ErrorCodes.Validation, "Answer must be between 0 and 3.");
        }

        var chars = Answers(instrument).ToCharArray();
        chars[item - 1] = value is { } v ? (char)('0' + v) : '-';
        if (instrument == "PHQ-9")
        {
            Phq9 = new string(chars);
        }
        else
        {
            Gad7 = new string(chars);
        }

        Status = IntakeStatus.Active;
        LastActivityAtUtc = clock.UtcNow;
    }

    /// <summary>Answered items as item → score, for the crisis gate and the clinician view.</summary>
    public Dictionary<int, int> Scores(string instrument) =>
        Answers(instrument).Select((c, i) => (c, i)).Where(x => x.c is >= '0' and <= '3').ToDictionary(x => x.i + 1, x => x.c - '0');

    /// <summary>Total score when every item was answered; null when any item was skipped or not asked.</summary>
    public int? Total(string instrument)
    {
        var answers = Answers(instrument);
        return answers.All(c => c is >= '0' and <= '3') ? answers.Sum(c => c - '0') : null;
    }

    public int AnsweredOrSkipped => (Phq9 + Gad7).Count(c => c != '?');

    public bool CanFinish => Status is IntakeStatus.Active or IntakeStatus.Paused && (PatientTurns >= MinTurnsToFinish || FormMode) && !(Phq9 + Gad7).Contains('?', StringComparison.Ordinal);

    /// <summary>0–100: 40 points for talking (up to six turns), 60 for the questionnaires.</summary>
    public int Progress =>
        Status is IntakeStatus.Review or IntakeStatus.Submitted ? 100 : (int)Math.Round((Math.Min(PatientTurns, 6) / 6.0 * 40) + (AnsweredOrSkipped / 16.0 * 60));

    public void Complete(byte[] summaryCipher, bool byAi, IClock clock)
    {
        if (!CanFinish)
        {
            throw new DomainException(ErrorCodes.IntakeNotFinished, $"Intake {Id} cannot be finished yet.");
        }

        SummaryCipher = summaryCipher;
        SummaryByAi = byAi;
        Status = IntakeStatus.Review;
        LastActivityAtUtc = clock.UtcNow;
    }

    public void UpdateSummary(byte[] summaryCipher, IClock clock)
    {
        if (Status != IntakeStatus.Review)
        {
            throw new DomainException(ErrorCodes.IntakeNotInReview, $"Intake {Id} summary is not editable.");
        }

        SummaryCipher = summaryCipher;
        LastActivityAtUtc = clock.UtcNow;
    }

    public void Submit(IClock clock)
    {
        if (Status != IntakeStatus.Review || SummaryCipher is null)
        {
            throw new DomainException(ErrorCodes.IntakeNotInReview, $"Intake {Id} is not ready to submit.");
        }

        Status = IntakeStatus.Submitted;
        SubmittedAtUtc = clock.UtcNow;
    }

    public void ApproveByTherapist(Guid therapistUserId, IClock clock)
    {
        if (Status != IntakeStatus.Submitted)
        {
            throw new DomainException(ErrorCodes.IntakeNotInReview, $"Intake {Id} was not submitted.");
        }

        TherapistApprovedAtUtc ??= clock.UtcNow;
        TherapistApprovedBy ??= therapistUserId;
    }

    private string Answers(string instrument) => instrument switch
    {
        "PHQ-9" => Phq9,
        "GAD-7" => Gad7,
        _ => throw new DomainException(ErrorCodes.Validation, $"Unknown instrument {instrument}."),
    };

    private void EnsureConversationOpen(IClock clock)
    {
        ExpireIfStale(clock);
        if (Status is not (IntakeStatus.Active or IntakeStatus.Paused))
        {
            throw new DomainException(ErrorCodes.IntakeClosed, $"Intake {Id} is {Status}.");
        }
    }
}
