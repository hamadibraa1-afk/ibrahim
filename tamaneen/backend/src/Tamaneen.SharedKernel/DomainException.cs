namespace Tamaneen.SharedKernel;

/// <summary>
/// A rule violation. <see cref="Code"/> is a stable key that the API turns into a localized message;
/// the exception message itself is English and only for logs.
/// </summary>
public class DomainException : Exception
{
    public DomainException(string code, string logMessage, params object[] arguments)
        : base(logMessage)
    {
        Code = code;
        Arguments = arguments;
    }

    public string Code { get; }

    public IReadOnlyList<object> Arguments { get; }
}

/// <summary>Stable error codes. Each one has an entry in the API resource files for ar and en.</summary>
public static class ErrorCodes
{
    public const string UnsupportedLanguage = "error.language.unsupported";
    public const string InvalidPseudonym = "error.pseudonym.invalid";
    public const string PatientDataShredded = "error.patient.shredded";
    public const string AssignmentAlreadyEnded = "error.assignment.ended";
    public const string CrisisAlreadyAcknowledged = "error.crisis.already_acknowledged";
    public const string CrisisClosed = "error.crisis.closed";
    public const string CrisisCloseNotAllowed = "error.crisis.close_not_allowed";
    public const string ReasonRequired = "error.reason.required";
    public const string NotFound = "error.not_found";
    public const string AiMessageInvalid = "error.ai.message_invalid";
}
