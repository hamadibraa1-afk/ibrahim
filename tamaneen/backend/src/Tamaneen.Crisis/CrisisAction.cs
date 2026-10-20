using Tamaneen.SharedKernel;

namespace Tamaneen.Crisis;

/// <summary>
/// What a responder did on a crisis event (S04): contacted the patient, started the local emergency
/// procedure, added a note, handed over. Free text is encrypted with the patient's key.
/// </summary>
public sealed class CrisisAction : Entity
{
    public static readonly IReadOnlySet<string> Kinds = new HashSet<string>(StringComparer.Ordinal) { "contacted", "local_emergency", "note", "handover" };

    private CrisisAction(Guid id, Guid crisisEventId, Guid patientId, string kind, Guid actorUserId, string actorRole, byte[]? textCipher, DateTimeOffset now)
        : base(id)
    {
        CrisisEventId = crisisEventId;
        PatientId = patientId;
        Kind = kind;
        ActorUserId = actorUserId;
        ActorRole = actorRole;
        TextCipher = textCipher;
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

    public DateTimeOffset OccurredAtUtc { get; private set; }

    public static CrisisAction Record(CrisisEvent evt, string kind, Guid actorUserId, string actorRole, byte[]? textCipher, IClock clock)
    {
        if (!Kinds.Contains(kind))
        {
            throw new DomainException(ErrorCodes.Validation, $"Unknown crisis action '{kind}'.");
        }

        if (evt.Status == CrisisEventStatus.Closed)
        {
            throw new DomainException(ErrorCodes.CrisisClosed, $"Crisis event {evt.Id} is closed.");
        }

        return new CrisisAction(Guid.NewGuid(), evt.Id, evt.PatientId, kind, actorUserId, actorRole, textCipher, clock.UtcNow);
    }
}
