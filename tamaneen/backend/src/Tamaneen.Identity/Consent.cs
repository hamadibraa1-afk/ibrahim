using Tamaneen.SharedKernel;

namespace Tamaneen.Identity;

/// <summary>Consent keys, versions and rules. Changing a text means a new version and asking again.</summary>
public static class ConsentCatalog
{
    public const string Platform = "platform";
    public const string Ai = "ai";
    public const string ShareWithTherapist = "share_with_therapist";
    public const string Research = "research";
    public const string EmergencyLocation = "emergency_location";

    public sealed record Definition(string Key, string Version, bool Required, bool Withdrawable);

    /// <summary>Platform and sharing are the service itself: withdrawing them means closing the account.</summary>
    public static readonly IReadOnlyList<Definition> All =
    [
        new(Platform, "1.0", Required: true, Withdrawable: false),
        new(Ai, "1.0", Required: true, Withdrawable: true),
        new(ShareWithTherapist, "1.0", Required: true, Withdrawable: false),
        new(Research, "1.0", Required: false, Withdrawable: true),
        new(EmergencyLocation, "1.0", Required: false, Withdrawable: true),
    ];

    public static Definition Get(string key) =>
        All.FirstOrDefault(d => d.Key == key) ?? throw new DomainException(ErrorCodes.ConsentUnknown, $"Unknown consent '{key}'.", key);
}

/// <summary>A patient's current answer to one consent. Every change is also written to the audit ledger.</summary>
public sealed class Consent
{
    private Consent(Guid patientId, string key, bool granted, string version, DateTimeOffset now)
    {
        PatientId = patientId;
        Key = key;
        Granted = granted;
        Version = version;
        UpdatedAtUtc = now;
    }

    // EF Core
    private Consent()
    {
        Key = string.Empty;
        Version = string.Empty;
    }

    public Guid PatientId { get; private set; }

    public string Key { get; private set; }

    public bool Granted { get; private set; }

    public string Version { get; private set; }

    public DateTimeOffset UpdatedAtUtc { get; private set; }

    public static Consent Record(Guid patientId, string key, bool granted, IClock clock)
    {
        var d = ConsentCatalog.Get(key);
        return new Consent(patientId, d.Key, granted, d.Version, clock.UtcNow);
    }

    /// <summary>Changes the answer. Required, non-withdrawable consents cannot be turned off here.</summary>
    public bool Change(bool granted, IClock clock)
    {
        var d = ConsentCatalog.Get(Key);
        if (!granted && d.Required && !d.Withdrawable)
        {
            throw new DomainException(ErrorCodes.ConsentRequired, $"Consent '{Key}' cannot be withdrawn.", Key);
        }

        if (Granted == granted && Version == d.Version)
        {
            return false;
        }

        Granted = granted;
        Version = d.Version;
        UpdatedAtUtc = clock.UtcNow;
        return true;
    }
}
