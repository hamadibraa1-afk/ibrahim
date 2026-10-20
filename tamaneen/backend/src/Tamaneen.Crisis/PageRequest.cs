using Tamaneen.SharedKernel;

namespace Tamaneen.Crisis;

/// <summary>
/// Transactional outbox row: "page tier N about event E". Written in the same transaction as the event, so a
/// crash between saving and paging cannot lose a page. Holds no patient content, only the pseudonym and level.
/// </summary>
public sealed class PageRequest : Entity
{
    private PageRequest(Guid id, Guid crisisEventId, byte tier, string pseudonymId, RiskLevel level, string language, DateTimeOffset createdAtUtc)
        : base(id)
    {
        CrisisEventId = crisisEventId;
        Tier = tier;
        PseudonymId = pseudonymId;
        Level = level;
        PatientLanguage = language;
        CreatedAtUtc = createdAtUtc;
        NextAttemptAtUtc = createdAtUtc;
    }

    // EF Core
    private PageRequest()
    {
        PseudonymId = string.Empty;
        PatientLanguage = SupportedLanguages.Default;
    }

    public Guid CrisisEventId { get; private set; }

    public byte Tier { get; private set; }

    public string PseudonymId { get; private set; }

    public RiskLevel Level { get; private set; }

    public string PatientLanguage { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public DateTimeOffset NextAttemptAtUtc { get; private set; }

    public DateTimeOffset? SentAtUtc { get; private set; }

    public int Attempts { get; private set; }

    public string? LastError { get; private set; }

    public static PageRequest For(CrisisEvent crisisEvent, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(crisisEvent);
        return new PageRequest(Guid.NewGuid(), crisisEvent.Id, crisisEvent.EscalationTier, crisisEvent.PseudonymId, crisisEvent.Level, crisisEvent.Language, clock.UtcNow);
    }

    public void MarkSent(IClock clock)
    {
        Attempts++;
        SentAtUtc = clock.UtcNow;
        LastError = null;
    }

    /// <summary>Retries quickly at first (5 s, 10 s, 20 s …) and never waits more than a minute: pages are urgent.</summary>
    public void MarkFailed(string error, IClock clock)
    {
        Attempts++;
        LastError = error.Length <= 500 ? error : error[..500];
        var delay = TimeSpan.FromSeconds(Math.Min(60, 5 * Math.Pow(2, Math.Min(Attempts - 1, 4))));
        NextAttemptAtUtc = clock.UtcNow + delay;
    }
}
