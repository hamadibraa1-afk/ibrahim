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

    /// <summary>Optional reason the patient gave for "not a fit" (PB-36): one of <see cref="DismissReasons"/>.</summary>
    public string? DismissReason { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public static MatchSuggestion Create(Guid patientId, Guid therapistUserId, int rank, IEnumerable<string> sharedSpecialties, IClock clock) =>
        new(Guid.NewGuid(), patientId, therapistUserId, rank, string.Join(',', sharedSpecialties), clock.UtcNow);

    /// <summary>Reasons the patient can pick; free text is not accepted so nothing here needs the crisis gate.</summary>
    public static readonly IReadOnlyList<string> DismissReasons = ["specialty", "gender", "language", "schedule", "other"];

    public void Dismiss(string? reason = null)
    {
        if (reason is not null && !DismissReasons.Contains(reason))
        {
            throw new DomainException(ErrorCodes.Validation, $"Unknown dismiss reason {reason}.").With("errors", new Dictionary<string, string[]> { ["reason"] = ["invalid"] });
        }

        Dismissed = true;
        DismissReason = reason;
    }
}
