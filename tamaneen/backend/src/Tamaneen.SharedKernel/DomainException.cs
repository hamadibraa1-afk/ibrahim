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

    /// <summary>Extra machine-readable fields for the client, e.g. remainingAttempts or retryAfterSeconds.</summary>
    public IDictionary<string, object> Extras { get; } = new Dictionary<string, object>(StringComparer.Ordinal);

    public DomainException With(string key, object value)
    {
        Extras[key] = value;
        return this;
    }
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
    public const string Maintenance = "error.maintenance";
    public const string AiMessageInvalid = "error.ai.message_invalid";
    public const string Validation = "error.validation";
    public const string InvalidCredentials = "error.auth.invalid_credentials";
    public const string AccountLocked = "error.auth.locked";
    public const string InvalidCode = "error.auth.invalid_code";
    public const string ChallengeExpired = "error.auth.challenge_expired";
    public const string ResendTooSoon = "error.auth.resend_too_soon";
    public const string WeakPassword = "error.auth.weak_password";
    public const string NotEligible = "error.auth.not_eligible";
    public const string SignupsPaused = "error.auth.signups_paused";
    public const string SessionRevoked = "error.auth.session_revoked";
    public const string MfaFailed = "error.auth.mfa_failed";
    public const string StepUpRequired = "error.auth.step_up_required";
    public const string TotpAlreadyEnabled = "error.totp.already_enabled";
    public const string TotpNotSetUp = "error.totp.not_set_up";
    public const string ConsentUnknown = "error.consent.unknown";
    public const string ConsentRequired = "error.consent.required";
    public const string ConsentsMissing = "error.consent.missing";
    public const string AiConsentWithdrawn = "error.consent.ai_withdrawn";
    public const string InvalidTime = "error.time.invalid";
    public const string InvalidTimeZone = "error.timezone.invalid";
    public const string InvalidCarePath = "error.path.invalid";
    public const string CrisisLocked = "error.crisis.locked";
    public const string CrisisNotAcknowledged = "error.crisis.not_acknowledged";
    public const string CrisisNotOwner = "error.crisis.not_owner";
    public const string CrisisNothingPending = "error.crisis.nothing_pending";
    public const string CrisisServiceUnavailable = "error.crisis.unavailable";
    public const string SlotUnavailable = "error.slot.unavailable";
    public const string HoldExpired = "error.slot.hold_expired";
    public const string AppointmentNotChangeable = "error.appointment.not_changeable";
    public const string AppointmentNotStarted = "error.appointment.not_started";
    public const string BookingNotEligible = "error.booking.not_eligible";
    public const string QuestionNotPending = "error.intake.question_not_pending";
    public const string IntakeNotFinished = "error.intake.not_finished";
    public const string IntakeNotInReview = "error.intake.not_in_review";
    public const string IntakeClosed = "error.intake.closed";
    public const string TriageAlreadyReviewed = "error.triage.already_reviewed";
    public const string HomeworkEnded = "error.homework.ended";
    public const string PasswordMismatch = "error.auth.password_mismatch";
    public const string DeletionBlockedByCrisis = "error.privacy.deletion_crisis_open";
    public const string TriageClaimedByOther = "error.triage.claimed";
    public const string TriageClaimRequired = "error.triage.claim_required";
    public const string TriageBelowCrisisFloor = "error.triage.below_crisis_floor";
    public const string TriageLoweringNeedsDecision = "error.triage.lowering_needs_decision";
    public const string TriageCoSignPending = "error.triage.cosign_pending";
    public const string TriageNoCoSignPending = "error.triage.no_cosign_pending";
    public const string TriageCoSignNotAllowed = "error.triage.cosign_not_allowed";
    public const string NoTherapistAvailable = "error.triage.no_therapist";
    public const string FileVersionRequired = "error.file.version_required";
    public const string FileVersionChanged = "error.file.version_changed";
    public const string FileApproved = "error.file.approved";
    public const string RiskNotHigher = "error.risk.not_higher";
}
