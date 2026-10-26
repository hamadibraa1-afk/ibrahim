using Tamaneen.SharedKernel;

namespace Tamaneen.Clinical.Scheduling;

/// <summary>Public directory entry of a therapist (P24, P25). Written in both languages by the therapist.</summary>
public sealed class TherapistProfile
{
    public static readonly IReadOnlySet<string> SpecialtyCodes = new HashSet<string>(StringComparer.Ordinal)
    {
        "anxiety", "stress", "sleep", "depression", "cbt", "mindfulness", "work_stress", "adjustment", "social_anxiety", "trauma",
    };

    private TherapistProfile(Guid userId)
    {
        UserId = userId;
        TitleAr = TitleEn = BioAr = BioEn = string.Empty;
        Specialties = Languages = string.Empty;
        Gender = "female";
    }

    // EF Core
    private TherapistProfile()
    {
        TitleAr = TitleEn = BioAr = BioEn = string.Empty;
        Specialties = Languages = string.Empty;
        Gender = "female";
    }

    public Guid UserId { get; private set; }

    public string TitleAr { get; private set; }

    public string TitleEn { get; private set; }

    public string BioAr { get; private set; }

    public string BioEn { get; private set; }

    /// <summary>"female" or "male".</summary>
    public string Gender { get; private set; }

    /// <summary>Comma-separated specialty codes (see <see cref="SpecialtyCodes"/>).</summary>
    public string Specialties { get; private set; }

    /// <summary>Comma-separated session languages, "ar,en".</summary>
    public string Languages { get; private set; }

    public bool LicenseVerified { get; private set; }

    public bool AcceptingNewPatients { get; private set; } = true;

    public int Capacity { get; private set; } = 25;

    public int SessionMinutes { get; private set; } = 50;

    public IReadOnlyList<string> SpecialtyList => Specialties.Split(',', StringSplitOptions.RemoveEmptyEntries);

    public IReadOnlyList<string> LanguageList => Languages.Split(',', StringSplitOptions.RemoveEmptyEntries);

    public static TherapistProfile Create(Guid userId, string titleAr, string titleEn, string bioAr, string bioEn, string gender, IEnumerable<string> specialties, IEnumerable<string> languages, bool licenseVerified, int capacity = 25)
    {
        var p = new TherapistProfile(userId);
        p.Update(titleAr, titleEn, bioAr, bioEn, gender, specialties, languages);
        p.LicenseVerified = licenseVerified;
        p.Capacity = capacity is > 0 and <= 200 ? capacity : throw new ArgumentOutOfRangeException(nameof(capacity));
        return p;
    }

    public void Update(string titleAr, string titleEn, string bioAr, string bioEn, string gender, IEnumerable<string> specialties, IEnumerable<string> languages)
    {
        var spec = specialties.Distinct().ToList();
        var langs = languages.Select(SupportedLanguages.Normalize).Distinct().ToList();
        if (spec.Count == 0 || spec.Any(s => !SpecialtyCodes.Contains(s)))
        {
            throw new ArgumentException("Unknown or missing specialty.", nameof(specialties));
        }

        if (gender is not ("female" or "male"))
        {
            throw new ArgumentException("Gender must be female or male.", nameof(gender));
        }

        TitleAr = titleAr.Trim();
        TitleEn = titleEn.Trim();
        BioAr = bioAr.Trim();
        BioEn = bioEn.Trim();
        Gender = gender;
        Specialties = string.Join(',', spec);
        Languages = string.Join(',', langs);
    }

    /// <summary>Session lengths S19 offers.</summary>
    public static readonly IReadOnlyList<int> SessionLengths = [30, 45, 50, 60, 90];

    /// <summary>S19: the length of every new bookable time. Booked sessions keep the length they were booked with.</summary>
    public void SetSessionMinutes(int minutes) =>
        SessionMinutes = SessionLengths.Contains(minutes) ? minutes : throw new ArgumentOutOfRangeException(nameof(minutes));

    /// <summary>S21: capacity may not go below the patients the therapist already cares for (SB-29).</summary>
    public void SetCapacity(int capacity, int currentPatients)
    {
        if (capacity is <= 0 or > 200)
        {
            throw new ArgumentOutOfRangeException(nameof(capacity));
        }

        if (capacity < currentPatients)
        {
            throw new DomainException(ErrorCodes.CapacityBelowCaseload, $"Capacity {capacity} is below the current {currentPatients} patients.", currentPatients);
        }

        Capacity = capacity;
    }

    /// <summary>Off: the therapist leaves new match suggestions and the directory; current patients are not affected.</summary>
    public void SetAcceptingNewPatients(bool accepting) => AcceptingNewPatients = accepting;

    public string Title(string language) => language == "en" ? TitleEn : TitleAr;

    public string Bio(string language) => language == "en" ? BioEn : BioAr;
}
