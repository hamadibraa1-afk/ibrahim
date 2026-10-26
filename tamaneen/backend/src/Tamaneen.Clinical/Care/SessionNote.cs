using Tamaneen.SharedKernel;

namespace Tamaneen.Clinical.Care;

public enum SessionNoteStatus
{
    Draft = 0,
    /// <summary>Signed by the therapist; read-only, addenda only.</summary>
    Locked = 1,
}

/// <summary>
/// The therapist's note for one session (S33), SOAP, DAP or free text. Encrypted with the patient's key and never
/// readable by the patient (row-level security). Once signed it is locked: only addenda can be added.
/// </summary>
public sealed class SessionNote : Entity
{
    public const int MaxFieldLength = 4000;

    /// <summary>Templates and their fields, in display order.</summary>
    public static readonly IReadOnlyDictionary<string, string[]> Templates = new Dictionary<string, string[]>(StringComparer.Ordinal)
    {
        ["soap"] = ["s", "o", "a", "p"],
        ["dap"] = ["d", "a", "p"],
        ["free"] = ["text"],
    };

    private SessionNote(Guid id, Guid patientId, Guid therapistUserId, Guid appointmentId, DateTimeOffset now)
        : base(id)
    {
        PatientId = patientId;
        TherapistUserId = therapistUserId;
        AppointmentId = appointmentId;
        CreatedAtUtc = now;
        UpdatedAtUtc = now;
        Template = "soap";
    }

    // EF Core
    private SessionNote()
    {
        Template = "soap";
        FieldsCipher = Array.Empty<byte>();
    }

    public Guid PatientId { get; private set; }

    public Guid TherapistUserId { get; private set; }

    public Guid AppointmentId { get; private set; }

    public string Template { get; private set; }

    /// <summary>Encrypted JSON object: field key to text.</summary>
    public byte[] FieldsCipher { get; private set; } = Array.Empty<byte>();

    /// <summary>Encrypted JSON array of up to three plain-language points for the patient.</summary>
    public byte[]? SummaryCipher { get; private set; }

    public bool ShareSummary { get; private set; }

    public SessionNoteStatus Status { get; private set; }

    /// <summary>Changes on every save; the ETag of the note (If-Match on save and sign).</summary>
    public int Revision { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public DateTimeOffset UpdatedAtUtc { get; private set; }

    public DateTimeOffset? SignedAtUtc { get; private set; }

    public Guid? SignedByUserId { get; private set; }

    public static SessionNote Start(Guid patientId, Guid therapistUserId, Guid appointmentId, IClock clock) =>
        new(Guid.NewGuid(), patientId, therapistUserId, appointmentId, clock.UtcNow);

    public void SaveDraft(string template, byte[] fieldsCipher, byte[]? summaryCipher, bool shareSummary, IClock clock)
    {
        EnsureDraft();
        if (!Templates.ContainsKey(template))
        {
            throw new DomainException(ErrorCodes.Validation, $"Unknown note template {template}.");
        }

        Template = template;
        FieldsCipher = fieldsCipher;
        SummaryCipher = summaryCipher;
        ShareSummary = shareSummary && summaryCipher is not null;
        UpdatedAtUtc = clock.UtcNow;
        Revision++;
    }

    public void Sign(Guid userId, IClock clock)
    {
        EnsureDraft();
        Status = SessionNoteStatus.Locked;
        SignedAtUtc = clock.UtcNow;
        SignedByUserId = userId;
        UpdatedAtUtc = clock.UtcNow;
        Revision++;
    }

    /// <summary>An addendum changes the note's version too, so a stale view is refreshed.</summary>
    public void Touch(IClock clock)
    {
        UpdatedAtUtc = clock.UtcNow;
        Revision++;
    }

    private void EnsureDraft()
    {
        if (Status == SessionNoteStatus.Locked)
        {
            throw new DomainException(ErrorCodes.NoteSigned, $"Note {Id} is signed.");
        }
    }
}

/// <summary>A dated addition to a signed note (the note itself never changes after signing).</summary>
public sealed class SessionNoteAddendum : Entity
{
    private SessionNoteAddendum(Guid id, Guid noteId, Guid patientId, Guid authorUserId, byte[] textCipher, DateTimeOffset now)
        : base(id)
    {
        NoteId = noteId;
        PatientId = patientId;
        AuthorUserId = authorUserId;
        TextCipher = textCipher;
        CreatedAtUtc = now;
    }

    // EF Core
    private SessionNoteAddendum()
    {
        TextCipher = Array.Empty<byte>();
    }

    public Guid NoteId { get; private set; }

    public Guid PatientId { get; private set; }

    public Guid AuthorUserId { get; private set; }

    public byte[] TextCipher { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public static SessionNoteAddendum Add(SessionNote note, Guid authorUserId, byte[] textCipher, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(note);
        if (note.Status != SessionNoteStatus.Locked)
        {
            throw new DomainException(ErrorCodes.NoteNotSigned, $"Note {note.Id} is still a draft; edit it instead.");
        }

        return new SessionNoteAddendum(Guid.NewGuid(), note.Id, note.PatientId, authorUserId, textCipher, clock.UtcNow);
    }
}

/// <summary>The three plain-language points a therapist shares with the patient when signing a note (no diagnosis).</summary>
public sealed class SessionSummary : Entity
{
    private SessionSummary(Guid id, Guid patientId, Guid appointmentId, Guid therapistUserId, byte[] pointsCipher, DateTimeOffset now)
        : base(id)
    {
        PatientId = patientId;
        AppointmentId = appointmentId;
        TherapistUserId = therapistUserId;
        PointsCipher = pointsCipher;
        SharedAtUtc = now;
    }

    // EF Core
    private SessionSummary()
    {
        PointsCipher = Array.Empty<byte>();
    }

    public Guid PatientId { get; private set; }

    public Guid AppointmentId { get; private set; }

    public Guid TherapistUserId { get; private set; }

    public byte[] PointsCipher { get; private set; }

    public DateTimeOffset SharedAtUtc { get; private set; }

    public static SessionSummary Share(SessionNote note, byte[] pointsCipher, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(note);
        return new SessionSummary(Guid.NewGuid(), note.PatientId, note.AppointmentId, note.TherapistUserId, pointsCipher, clock.UtcNow);
    }
}
