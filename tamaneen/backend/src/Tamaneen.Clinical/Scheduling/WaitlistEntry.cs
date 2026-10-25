using Tamaneen.SharedKernel;

namespace Tamaneen.Clinical.Scheduling;

public enum WaitlistStatus
{
    /// <summary>In the queue, nothing matching is free yet.</summary>
    Waiting = 0,

    /// <summary>A matching time opened and the patient was told (P56 «تم التوفر»).</summary>
    Notified = 1,

    /// <summary>The patient left the waitlist. Kept for the queue history.</summary>
    Left = 2,
}

/// <summary>
/// A patient waiting for a time (P56). Joined from P25 (one therapist, no bookable times), P24 (no therapist fits
/// the filters) or P07 (no suggested therapist has free capacity). The preferences are the P47 ones: therapist
/// gender, session language, times of day and days. The queue is ordered by risk first, then by how long the
/// patient has waited (guide §10.2). One active entry per patient; changing the preferences keeps the place.
/// </summary>
public sealed class WaitlistEntry : Entity
{
    public static readonly IReadOnlyList<string> Genders = ["any", "female", "male"];

    public static readonly IReadOnlyList<string> Languages = ["any", "ar", "en"];

    public static readonly IReadOnlyList<string> TimesOfDay = ["morning", "afternoon", "evening"];

    public static readonly IReadOnlyList<string> DayKinds = ["weekdays", "weekend"];

    private WaitlistEntry(Guid id, Guid patientId, RiskLevel riskLevel, DateTimeOffset now)
        : base(id)
    {
        PatientId = patientId;
        RiskLevel = riskLevel;
        JoinedAtUtc = now;
        Gender = Language = "any";
        Times = Days = string.Empty;
    }

    // EF Core
    private WaitlistEntry()
    {
        Gender = Language = "any";
        Times = Days = string.Empty;
    }

    public Guid PatientId { get; private set; }

    /// <summary>A single therapist (joined from P25), or null for any therapist who fits the preferences.</summary>
    public Guid? TherapistUserId { get; private set; }

    /// <summary>"any", "female" or "male".</summary>
    public string Gender { get; private set; }

    /// <summary>"any", "ar" or "en".</summary>
    public string Language { get; private set; }

    /// <summary>Comma-separated <see cref="TimesOfDay"/>; empty means any time.</summary>
    public string Times { get; private set; }

    /// <summary>Comma-separated <see cref="DayKinds"/>; empty means any day.</summary>
    public string Days { get; private set; }

    /// <summary>The patient's IANA time zone, in which the times of day are read.</summary>
    public string TimeZone { get; private set; } = "Asia/Riyadh";

    /// <summary>The level of the latest triage when the patient joined (or changed preferences); orders the queue.</summary>
    public RiskLevel RiskLevel { get; private set; }

    public WaitlistStatus Status { get; private set; }

    public DateTimeOffset JoinedAtUtc { get; private set; }

    public DateTimeOffset? NotifiedAtUtc { get; private set; }

    /// <summary>The slot that triggered the notice, so the app can offer its therapist.</summary>
    public Guid? NotifiedSlotId { get; private set; }

    public DateTimeOffset? LeftAtUtc { get; private set; }

    public IReadOnlyList<string> TimeList => Times.Split(',', StringSplitOptions.RemoveEmptyEntries);

    public IReadOnlyList<string> DayList => Days.Split(',', StringSplitOptions.RemoveEmptyEntries);

    public bool Active => Status != WaitlistStatus.Left;

    public static WaitlistEntry Join(Guid patientId, Guid? therapistUserId, string? gender, string? language, IEnumerable<string>? times, IEnumerable<string>? days, RiskLevel riskLevel, string timeZone, IClock clock)
    {
        var entry = new WaitlistEntry(Guid.NewGuid(), patientId, riskLevel, clock.UtcNow);
        entry.SetPreferences(therapistUserId, gender, language, times, days, riskLevel, timeZone);
        return entry;
    }

    /// <summary>New preferences keep the place in the queue and start waiting again (a notice for old preferences no longer applies).</summary>
    public void SetPreferences(Guid? therapistUserId, string? gender, string? language, IEnumerable<string>? times, IEnumerable<string>? days, RiskLevel riskLevel, string timeZone)
    {
        var g = string.IsNullOrWhiteSpace(gender) ? "any" : gender.Trim();
        var l = string.IsNullOrWhiteSpace(language) ? "any" : language.Trim();
        var t = (times ?? []).Select(x => x.Trim()).Distinct(StringComparer.Ordinal).ToList();
        var d = (days ?? []).Select(x => x.Trim()).Distinct(StringComparer.Ordinal).ToList();
        var errors = new Dictionary<string, string[]>();
        if (!Genders.Contains(g))
        {
            errors["gender"] = ["invalid"];
        }

        if (!Languages.Contains(l))
        {
            errors["language"] = ["invalid"];
        }

        if (t.Any(x => !TimesOfDay.Contains(x)))
        {
            errors["timesOfDay"] = ["invalid"];
        }

        if (d.Any(x => !DayKinds.Contains(x)))
        {
            errors["days"] = ["invalid"];
        }

        if (errors.Count > 0)
        {
            throw new DomainException(ErrorCodes.Validation, "Invalid waitlist preferences.").With("errors", errors);
        }

        TherapistUserId = therapistUserId;
        Gender = g;
        Language = l;
        Times = string.Join(',', TimesOfDay.Where(t.Contains));
        Days = string.Join(',', DayKinds.Where(d.Contains));
        RiskLevel = riskLevel;
        TimeZone = string.IsNullOrWhiteSpace(timeZone) ? "Asia/Riyadh" : timeZone;
        Status = WaitlistStatus.Waiting;
        NotifiedAtUtc = null;
        NotifiedSlotId = null;
        LeftAtUtc = null;
    }

    public void Leave(IClock clock)
    {
        if (Status != WaitlistStatus.Left)
        {
            Status = WaitlistStatus.Left;
            LeftAtUtc = clock.UtcNow;
        }
    }

    public void MarkNotified(Guid slotId, IClock clock)
    {
        if (Status == WaitlistStatus.Waiting)
        {
            Status = WaitlistStatus.Notified;
            NotifiedAtUtc = clock.UtcNow;
            NotifiedSlotId = slotId;
        }
    }

    /// <summary>
    /// Whether a free slot of this therapist fits the entry. Times of day are read in the patient's time zone:
    /// morning 06:00–12:00, afternoon 12:00–17:00, evening 17:00–23:00. The weekend is Friday and Saturday.
    /// </summary>
    public bool Fits(TherapistProfile therapist, DateTimeOffset slotStartUtc)
    {
        if (TherapistUserId is { } only && only != therapist.UserId)
        {
            return false;
        }

        if (Gender != "any" && therapist.Gender != Gender)
        {
            return false;
        }

        if (Language != "any" && !therapist.LanguageList.Contains(Language))
        {
            return false;
        }

        var local = TimeZoneInfo.ConvertTime(slotStartUtc, Zone(TimeZone));
        var times = TimeList;
        if (times.Count > 0 && !times.Contains(TimeOfDay(local.Hour)))
        {
            return false;
        }

        var days = DayList;
        var weekend = local.DayOfWeek is DayOfWeek.Friday or DayOfWeek.Saturday;
        return days.Count == 0 || days.Contains(weekend ? "weekend" : "weekdays");
    }

    /// <summary>The pilot region's fixed UTC+3 when the zone is unknown on this host.</summary>
    private static TimeZoneInfo Zone(string id)
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(id);
        }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            return TimeZoneInfo.CreateCustomTimeZone("Arabia", TimeSpan.FromHours(3), "Arabia", "Arabia");
        }
    }

    public static string TimeOfDay(int hour) => hour switch
    {
        >= 6 and < 12 => "morning",
        >= 12 and < 17 => "afternoon",
        >= 17 and < 23 => "evening",
        _ => "night",
    };
}
