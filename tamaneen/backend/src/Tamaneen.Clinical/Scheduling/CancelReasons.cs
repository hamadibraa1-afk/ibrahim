namespace Tamaneen.Clinical.Scheduling;

/// <summary>
/// The cancel reasons a patient picks from on P28 (PB-45). Codes only, no free text, so nothing here needs the crisis gate.
/// Stands in for the admin Catalogs list (S47) until that exists; labels live in the client's messages.
/// </summary>
public static class CancelReasons
{
    public const int MaxLength = 32;

    public static readonly IReadOnlySet<string> Patient = new HashSet<string>(StringComparer.Ordinal)
    {
        "schedule_conflict",
        "feeling_better",
        "unwell",
        "therapist_fit",
        "other",
    };

    /// <summary>
    /// Reasons a therapist picks on S18 (SB-26). The patient never sees which one (P67 only says «لظرف طارئ»);
    /// supervisors and the audit log do.
    /// </summary>
    public static readonly IReadOnlySet<string> Therapist = new HashSet<string>(StringComparer.Ordinal)
    {
        "emergency",
        "illness",
        "schedule_conflict",
        "technical",
        "other",
    };
}
