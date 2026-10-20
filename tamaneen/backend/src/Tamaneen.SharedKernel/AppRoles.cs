namespace Tamaneen.SharedKernel;

/// <summary>Role names used in tokens, authorisation policies and the SQL row-level security predicate.</summary>
public static class AppRoles
{
    public const string Patient = "Patient";
    public const string Therapist = "Therapist";
    public const string Supervisor = "Supervisor";
    public const string ClinicalLead = "ClinicalLead";
    public const string Admin = "Admin";

    /// <summary>Service identity used by Crisis.Worker. It reads patient rows to evaluate and page.</summary>
    public const string CrisisService = "CrisisService";

    /// <summary>Service identity of the data-request job that executes approved deletions (crypto-shredding).</summary>
    public const string DataProtectionService = "DataProtectionService";

    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.Ordinal)
    {
        Patient, Therapist, Supervisor, ClinicalLead, Admin, CrisisService, DataProtectionService,
    };

    /// <summary>Roles that may read clinical content. Admin is deliberately absent.</summary>
    public static readonly IReadOnlySet<string> Clinical = new HashSet<string>(StringComparer.Ordinal)
    {
        Therapist, Supervisor, ClinicalLead, CrisisService,
    };

    public static bool IsKnown(string? role) => role is not null && All.Contains(role);

    /// <summary>Service identities never belong to a person and cannot be given to a user account.</summary>
    public static bool IsService(string? role) => role is CrisisService or DataProtectionService;
}
