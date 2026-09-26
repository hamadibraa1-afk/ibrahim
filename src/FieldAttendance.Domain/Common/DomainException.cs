namespace FieldAttendance.Domain.Common;

/// <summary>
/// Raised when a business rule is violated. <see cref="Code"/> is a stable,
/// machine-readable key the API maps to a translated message (ar/en).
/// </summary>
public sealed class DomainException : Exception
{
    public DomainException(string code, string message) : base(message) => Code = code;

    public string Code { get; }
}
