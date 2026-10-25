using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Tamaneen.AiGateway.Deidentification;
using Tamaneen.Api.Gate;
using Tamaneen.Identity;
using Tamaneen.Infrastructure.Persistence;
using Tamaneen.Infrastructure.Security;
using Tamaneen.SharedKernel;

namespace Tamaneen.Api.Patients;

/// <summary>Result of a gated write as the patient app sees it (GateOutcome in the contract).</summary>
public sealed record GateOutcomeDto(bool Crisis, int Level, string? Script);

/// <summary>
/// Everything a patient endpoint needs about the caller: their id and language, field encryption with
/// their own key, and the crisis gate. All queries run as the patient, so row-level security applies.
/// </summary>
public sealed class PatientContext(ICurrentUser user, TamaneenDbContext db, IPatientDataProtector protector, CrisisGateClient gate)
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public Guid PatientId => user.PatientId ?? throw new DomainException(ErrorCodes.NotFound, "Caller is not a patient.");

    public string Language => user.Language;

    public TamaneenDbContext Db => db;

    public Task<byte[]> EncryptAsync(string field, string text, CancellationToken ct) => protector.EncryptAsync(PatientId, field, text, ct);

    public async Task<byte[]?> EncryptOptionalAsync(string field, string? text, CancellationToken ct) =>
        string.IsNullOrWhiteSpace(text) ? null : await protector.EncryptAsync(PatientId, field, text.Trim(), ct).ConfigureAwait(false);

    public Task<string> DecryptAsync(string field, byte[] cipher, CancellationToken ct) => protector.DecryptAsync(PatientId, field, cipher, ct);

    public async Task<string?> DecryptOptionalAsync(string field, byte[]? cipher, CancellationToken ct) =>
        cipher is null ? null : await protector.DecryptAsync(PatientId, field, cipher, ct).ConfigureAwait(false);

    public async Task<T?> DecryptJsonAsync<T>(string field, byte[]? cipher, CancellationToken ct)
        where T : class =>
        cipher is null ? null : JsonSerializer.Deserialize<T>(await protector.DecryptAsync(PatientId, field, cipher, ct).ConfigureAwait(false), Json);

    public Task<byte[]> EncryptJsonAsync<T>(string field, T value, CancellationToken ct) =>
        protector.EncryptAsync(PatientId, field, JsonSerializer.Serialize(value, Json), ct);

    public async Task<PatientProfile> ProfileAsync(CancellationToken ct) =>
        await db.PatientProfiles.FirstOrDefaultAsync(p => p.PatientId == PatientId, ct).ConfigureAwait(false)
            ?? throw new DomainException(ErrorCodes.NotFound, "Patient profile missing.");

    public async Task<UserAccount> AccountAsync(CancellationToken ct) =>
        await db.UserAccounts.FirstAsync(a => a.Id == PatientId, ct).ConfigureAwait(false);

    public Task<bool> IsChatLockedAsync(CancellationToken ct) =>
        db.ChatLocks.AnyAsync(l => l.PatientId == PatientId && l.ReleasedAtUtc == null, ct);

    /// <summary>Throws 409 error.crisis.locked while a crisis event holds the chat.</summary>
    public async Task EnsureNotLockedAsync(CancellationToken ct)
    {
        if (await IsChatLockedAsync(ct).ConfigureAwait(false))
        {
            throw new DomainException(ErrorCodes.CrisisLocked, "Chat is locked by a crisis event.");
        }
    }

    public async Task<bool> HasConsentAsync(string key, CancellationToken ct) =>
        await db.Consents.AnyAsync(c => c.PatientId == PatientId && c.Key == key && c.Granted, ct).ConfigureAwait(false);

    /// <summary>
    /// Runs the crisis gate. If the safety check cannot run, nothing else happens: the caller gets a 503
    /// that tells the patient how to get help right now.
    /// </summary>
    public async Task<GateVerdict> GateAsync(string? text, GateAnswers? answers, string source, CancellationToken ct)
    {
        var verdict = await gate.EvaluateAsync(PatientId, Language, text, answers, source, ct).ConfigureAwait(false);
        if (verdict.Unavailable)
        {
            throw new DomainException(ErrorCodes.CrisisServiceUnavailable, "Crisis gate unavailable.");
        }

        return verdict;
    }

    public static GateOutcomeDto Outcome(GateVerdict verdict) => new(verdict.Crisis, (int)verdict.Level, verdict.Crisis ? verdict.Script : null);

    /// <summary>Names the de-identifier must always hide for this patient: their nickname and trusted contact.</summary>
    public async Task<KnownIdentifiers> KnownIdentifiersAsync(CancellationToken ct)
    {
        var profile = await ProfileAsync(ct).ConfigureAwait(false);
        var names = new List<string>();
        if (await DecryptOptionalAsync(Fields.DisplayName, profile.DisplayNameCipher, ct).ConfigureAwait(false) is { } nickname)
        {
            names.Add(nickname);
        }

        if (await DecryptJsonAsync<SafetyInfoData>(Fields.SafetyInfo, profile.EmergencyContactCipher, ct).ConfigureAwait(false) is { TrustedName: { } trusted })
        {
            names.Add(trusted);
        }

        return new KnownIdentifiers(names, []);
    }
}

/// <summary>Field names bound into each ciphertext's associated data. Never rename one: old values would stop decrypting.</summary>
public static class Fields
{
    public const string DisplayName = Auth.AuthService.DisplayNameField;
    public const string Contact = Auth.AuthService.ContactField;
    public const string SafetyInfo = ProtectedFields.EmergencyContact;
    public const string IntakeMessage = "clinical.intake_message";
    public const string IntakeSummary = "clinical.intake_summary";
    public const string TriageAddenda = "clinical.triage_addenda";
    public const string ScreeningAnswers = "clinical.screening_answers";
    public const string MoodNote = "clinical.mood_note";
    public const string HomeworkTitle = "clinical.homework_title";
    public const string HomeworkInstructions = "clinical.homework_instructions";
    public const string CheckinNote = "clinical.checkin_note";
    public const string CheckinFeedback = "clinical.checkin_feedback";
    public const string SafetyPlan = "clinical.safety_plan";
    public const string CrisisNote = "crisis.action_text";
    public const string FileEdits = "clinical.file_edits";
    public const string FileFeedback = "clinical.file_feedback";
    public const string IdempotentResponse = "clinical.idempotent_response";
    public const string TherapistCancelNote = "clinical.therapist_cancel_note";
}

public sealed record SafetyInfoData(string? ContactPhone, string? TrustedName, string? TrustedPhone);
