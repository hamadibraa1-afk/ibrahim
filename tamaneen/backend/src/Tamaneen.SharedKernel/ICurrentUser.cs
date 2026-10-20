namespace Tamaneen.SharedKernel;

/// <summary>
/// The caller of the current request or job. Its values are pushed into SQL Server SESSION_CONTEXT
/// so row-level security filters every query, whatever the application code forgets.
/// </summary>
public interface ICurrentUser
{
    Guid? UserId { get; }

    string? Role { get; }

    /// <summary>The patient id when the caller is a patient; otherwise null.</summary>
    Guid? PatientId { get; }

    /// <summary>The caller's language for messages and AI replies ("ar" or "en").</summary>
    string Language { get; }
}

/// <summary>A fixed identity for background work and tests.</summary>
public sealed record StaticCurrentUser(Guid? UserId, string? Role, Guid? PatientId, string Language = SupportedLanguages.Default)
    : ICurrentUser
{
    public static StaticCurrentUser Anonymous { get; } = new(null, null, null);

    public static StaticCurrentUser ForPatient(Guid patientId, string language = SupportedLanguages.Default) =>
        new(patientId, AppRoles.Patient, patientId, language);

    public static StaticCurrentUser ForStaff(Guid userId, string role, string language = SupportedLanguages.Default) =>
        new(userId, role, null, language);
}
