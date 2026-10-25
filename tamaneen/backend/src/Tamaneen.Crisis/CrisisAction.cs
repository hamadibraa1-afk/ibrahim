using Tamaneen.SharedKernel;

namespace Tamaneen.Crisis;

/// <summary>
/// What a responder did on a crisis event (S04): called the patient and how the call went, contacted the
/// patient, started the local emergency procedure, added a note, referred to the on-call (S01), handed over,
/// asked for or gave the clinical lead's co-sign. Free text is encrypted with the patient's key.
/// </summary>
public sealed class CrisisAction : Entity
{
    /// <summary>Kinds a responder posts on POST /v1/crisis/events/{id}/actions.</summary>
    public static readonly IReadOnlySet<string> RequestKinds = new HashSet<string>(StringComparer.Ordinal)
    {
        "contacted", "local_emergency", "note", "handover", "call_attempt", "call_outcome", "refer",
    };

    /// <summary>Every kind on the timeline, including those the handover and close endpoints record.</summary>
    public static readonly IReadOnlySet<string> Kinds = new HashSet<string>(RequestKinds, StringComparer.Ordinal)
    {
        "handover_requested", "handover_accepted", "handover_declined", "close_requested", "close_cosigned",
    };

    /// <summary>How a call attempt ended (SB-39). Only "answered" counts as reaching the patient.</summary>
    public static readonly IReadOnlySet<string> CallOutcomes = new HashSet<string>(StringComparer.Ordinal) { "answered", "no_answer", "voicemail", "wrong_number" };

    private CrisisAction(Guid id, Guid crisisEventId, Guid patientId, string kind, Guid actorUserId, string actorRole, byte[]? textCipher, string? detail, Guid? targetUserId, DateTimeOffset now)
        : base(id)
    {
        CrisisEventId = crisisEventId;
        PatientId = patientId;
        Kind = kind;
        ActorUserId = actorUserId;
        ActorRole = actorRole;
        TextCipher = textCipher;
        Detail = detail;
        TargetUserId = targetUserId;
        OccurredAtUtc = now;
    }

    // EF Core
    private CrisisAction()
    {
        Kind = string.Empty;
        ActorRole = string.Empty;
    }

    public Guid CrisisEventId { get; private set; }

    public Guid PatientId { get; private set; }

    public string Kind { get; private set; }

    public Guid ActorUserId { get; private set; }

    public string ActorRole { get; private set; }

    public byte[]? TextCipher { get; private set; }

    /// <summary>A code, never patient text: the call outcome for call_outcome.</summary>
    public string? Detail { get; private set; }

    /// <summary>The colleague a handover was offered to.</summary>
    public Guid? TargetUserId { get; private set; }

    public DateTimeOffset OccurredAtUtc { get; private set; }

    public static CrisisAction Record(CrisisEvent evt, string kind, Guid actorUserId, string actorRole, byte[]? textCipher, IClock clock) =>
        Record(evt, kind, actorUserId, actorRole, textCipher, null, null, clock);

    public static CrisisAction Record(CrisisEvent evt, string kind, Guid actorUserId, string actorRole, byte[]? textCipher, string? detail, Guid? targetUserId, IClock clock)
    {
        if (!Kinds.Contains(kind))
        {
            throw new DomainException(ErrorCodes.Validation, $"Unknown crisis action '{kind}'.");
        }

        if (kind == "call_outcome" && (detail is null || !CallOutcomes.Contains(detail)))
        {
            throw new DomainException(ErrorCodes.Validation, $"Unknown call outcome '{detail}'.");
        }

        // The co-sign closes the event in the same unit of work; everything else needs it still open.
        if (evt.Status == CrisisEventStatus.Closed && kind != "close_cosigned")
        {
            throw new DomainException(ErrorCodes.CrisisClosed, $"Crisis event {evt.Id} is closed.");
        }

        return new CrisisAction(Guid.NewGuid(), evt.Id, evt.PatientId, kind, actorUserId, actorRole, textCipher, detail, targetUserId, clock.UtcNow);
    }
}
