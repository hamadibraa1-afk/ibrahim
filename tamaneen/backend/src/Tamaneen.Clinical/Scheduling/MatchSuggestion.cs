using Tamaneen.SharedKernel;

namespace Tamaneen.Clinical.Scheduling;

/// <summary>One of the (up to) three therapists suggested after triage review (P07).</summary>
public sealed class MatchSuggestion : Entity
{
    private MatchSuggestion(Guid id, Guid patientId, Guid therapistUserId, int rank, string sharedSpecialties, DateTimeOffset now)
        : base(id)
    {
        PatientId = patientId;
        TherapistUserId = therapistUserId;
        Rank = rank;
        SharedSpecialties = sharedSpecialties;
        CreatedAtUtc = now;
    }

    // EF Core
    private MatchSuggestion()
    {
        SharedSpecialties = string.Empty;
    }

    public Guid PatientId { get; private set; }

    public Guid TherapistUserId { get; private set; }

    public int Rank { get; private set; }

    public string SharedSpecialties { get; private set; }

    public bool Dismissed { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public static MatchSuggestion Create(Guid patientId, Guid therapistUserId, int rank, IEnumerable<string> sharedSpecialties, IClock clock) =>
        new(Guid.NewGuid(), patientId, therapistUserId, rank, string.Join(',', sharedSpecialties), clock.UtcNow);

    public void Dismiss() => Dismissed = true;
}
