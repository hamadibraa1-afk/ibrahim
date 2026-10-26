using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Tamaneen.Api.Clinical;
using Tamaneen.Api.Gate;
using Tamaneen.Api.Patients;
using Tamaneen.Clinical.Intake;
using Tamaneen.Clinical.Scheduling;
using Tamaneen.Crisis;
using Tamaneen.Hosting;
using Tamaneen.Infrastructure.Auditing;
using Tamaneen.Infrastructure.Persistence;
using Tamaneen.Infrastructure.Security;
using Tamaneen.SharedKernel;

namespace Tamaneen.Api.Endpoints;

/// <summary>
/// S02, the clinical file and its evidence inspector. Every read of the file or of a statement's sources writes an
/// audit row. Queries run as the signed-in clinician, so a therapist only reaches assigned patients (row-level
/// security; anyone else gets 404). Only the treating therapist edits, dismisses, rates and approves; supervisors and
/// the clinical lead read the file from triage (S03) and may raise the risk or a crisis.
/// </summary>
public static class StaffFileEndpoints
{
    /// <summary>Statements of the clinician file, in display order. "hypotheses" is never shown to the patient.</summary>
    public static readonly string[] StatementKeys = ["concern", "goals", "supports", "priorCare", "hypotheses"];

    public const string Hypotheses = "hypotheses";

    public sealed record ScoreItemDto(int Item, string Text, int? Value);

    public sealed record InstrumentDto(string Instrument, int? Total, string? Severity, int? Item9, string Version, IReadOnlyList<ScoreItemDto> Items);

    public sealed record CssrsItemDto(int Index, string Text, bool? Answer);

    public sealed record CssrsDto(bool Positive, int Level, string Version, DateTimeOffset CompletedAt, IReadOnlyList<CssrsItemDto> Items);

    public sealed record StatementDto(string Key, string Text, bool EditedByPatient, bool EditedByTherapist, bool Hypothesis, int SourceCount);

    public sealed record TranscriptTurnDto(Guid Id, int Turn, string Role, string Text, DateTimeOffset At);

    public sealed record CorrectionDto(string Key, string? AiText, string PatientText);

    public sealed record ApprovalDto(bool Approved, string? ApprovedBy, DateTimeOffset? ApprovedAt);

    public sealed record FileFeedbackDto(int? Rating, string? Comment);

    public sealed record PatientFileDto(
        string PseudonymId,
        int RiskLevel,
        int CrisisFloor,
        DateTimeOffset? FirstSessionAt,
        string Path,
        string Version,
        bool GeneratedByAi,
        IReadOnlyList<StatementDto> Statements,
        IReadOnlyList<TranscriptTurnDto> Transcript,
        bool TranscriptRemoved,
        InstrumentDto? Phq9,
        InstrumentDto? Gad7,
        CssrsDto? Cssrs,
        IReadOnlyList<CorrectionDto> Corrections,
        IReadOnlyList<string> Addenda,
        ApprovalDto Approval,
        FileFeedbackDto Feedback,
        bool CanEdit,
        DateTimeOffset? SubmittedAt);

    public sealed record EvidenceDto(string StatementKey, IReadOnlyList<Guid> MessageIds, IReadOnlyList<int> Turns, bool Removed);

    public sealed record ApproveFileRequest(Dictionary<string, string>? Edits);

    public sealed record DismissRequest(string? StatementKey, string? Reason);

    public sealed record FileFeedbackRequest(int Rating, string? Comment);

    public sealed record RiskFlagRequest(int RiskLevel, string? Reason);

    public sealed record RiskFlagResponse(int RiskLevel);

    public sealed record ManualCrisisRequest(int Level, string? Note);

    public sealed record ManualCrisisResponse(Guid CrisisEventId, int Level);

    public sealed record CaseloadItemDto(string PseudonymId, int RiskLevel, bool FileApproved, DateTimeOffset? NextSessionAt);

    private sealed record FileContext(Guid PatientId, string PseudonymId, TriageCase Triage, IntakeSession? Intake, IntakeEndpoints.StoredSummary? Summary, FileReview? Review, List<TranscriptTurn> Transcript);

    public static IEndpointRouteBuilder MapStaffFileEndpoints(this IEndpointRouteBuilder app)
    {
        var clinical = app.MapGroup("/v1/staff").RequireAuthorization(AuthSetup.ClinicalPolicy);
        clinical.MapGet("/patients", CaseloadAsync);
        clinical.MapGet("/patients/{pseudonymId}/file", FileAsync);
        clinical.MapGet("/patients/{pseudonymId}/evidence/{statementKey}", EvidenceAsync);
        clinical.MapPost("/patients/{pseudonymId}/risk-flag", RiskFlagAsync);
        clinical.MapPost("/patients/{pseudonymId}/crisis", ManualCrisisAsync);

        var therapist = app.MapGroup("/v1/staff").RequireAuthorization(p => p.RequireAuthenticatedUser().RequireRole(AppRoles.Therapist));
        therapist.MapPost("/patients/{pseudonymId}/file/approve", ApproveAsync);
        therapist.MapPost("/patients/{pseudonymId}/file/dismiss-item", DismissAsync);
        therapist.MapPost("/patients/{pseudonymId}/file-feedback", FeedbackAsync);
        return app;
    }

    /// <summary>"My patients": the therapist's active assignments (supervisors see every assigned patient).</summary>
    internal static async Task<Ok<List<CaseloadItemDto>>> CaseloadAsync(ICurrentUser user, TamaneenDbContext db, IClock clock, CancellationToken ct)
    {
        var query = db.CareAssignments.AsNoTracking().Where(a => a.EndedAtUtc == null);
        if (user.Role == AppRoles.Therapist)
        {
            query = query.Where(a => a.TherapistUserId == user.UserId);
        }

        var ids = await query.Select(a => a.PatientId).Distinct().ToListAsync(ct).ConfigureAwait(false);
        var pseudonyms = await db.UserAccounts.AsNoTracking().Where(a => ids.Contains(a.Id)).ToDictionaryAsync(a => a.Id, a => a.PseudonymId, ct).ConfigureAwait(false);
        var triage = (await db.TriageCases.AsNoTracking().Where(t => ids.Contains(t.PatientId) && t.Path != TriageCase.ChangePath).ToListAsync(ct).ConfigureAwait(false))
            .GroupBy(t => t.PatientId).ToDictionary(g => g.Key, g => g.OrderByDescending(t => t.SubmittedAtUtc).First());
        var intakeIds = triage.Values.Where(t => t.IntakeId is not null).Select(t => t.IntakeId!.Value).ToList();
        var approvedIntakes = await db.IntakeSessions.AsNoTracking().Where(i => intakeIds.Contains(i.Id) && i.TherapistApprovedAtUtc != null).Select(i => i.Id).ToListAsync(ct).ConfigureAwait(false);
        var floors = await CrisisFloorsAsync(db, ids, clock, ct).ConfigureAwait(false);
        var now = clock.UtcNow;
        var next = (await db.Appointments.AsNoTracking().Where(a => ids.Contains(a.PatientId) && a.Status == AppointmentStatus.Confirmed && a.StartsAtUtc > now)
            .Select(a => new { a.PatientId, a.StartsAtUtc }).ToListAsync(ct).ConfigureAwait(false))
            .GroupBy(a => a.PatientId).ToDictionary(g => g.Key, g => g.Min(a => a.StartsAtUtc));
        var items = ids.Where(pseudonyms.ContainsKey).Select(id =>
        {
            triage.TryGetValue(id, out var t);
            var risk = Math.Max((int)(t?.RiskLevel ?? RiskLevel.Routine), (int)floors.GetValueOrDefault(id));
            var approved = t is not null && (t.IntakeId is { } iid ? approvedIntakes.Contains(iid) : t.ReviewedAtUtc is not null);
            return new CaseloadItemDto(pseudonyms[id], risk, approved, next.TryGetValue(id, out var at) ? at : null);
        }).OrderByDescending(i => i.RiskLevel).ThenBy(i => i.NextSessionAt ?? DateTimeOffset.MaxValue).ToList();
        return TypedResults.Ok(items);
    }

    internal static async Task<Ok<PatientFileDto>> FileAsync(string pseudonymId, HttpContext http, ICurrentUser user, TamaneenDbContext db, IPatientDataProtector protector, QuestionnaireCatalog catalog, IAuditLog audit, IClock clock, CancellationToken ct)
    {
        var file = await LoadAsync(pseudonymId, db, protector, ct).ConfigureAwait(false);
        audit.Append("patient.file_opened", "TriageCase", file.Triage.Id.ToString(), file.PseudonymId);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        var dto = await BuildAsync(file, user, db, protector, catalog, clock, ct).ConfigureAwait(false);
        http.Response.Headers.ETag = $"\"{dto.Version}\"";
        return TypedResults.Ok(dto);
    }

    /// <summary>Source messages of one statement (SB-12). After the 90-day purge the answer says "removed".</summary>
    internal static async Task<Ok<EvidenceDto>> EvidenceAsync(string pseudonymId, string statementKey, TamaneenDbContext db, IPatientDataProtector protector, IAuditLog audit, CancellationToken ct)
    {
        if (!StatementKeys.Contains(statementKey))
        {
            throw new DomainException(ErrorCodes.NotFound, $"Unknown statement {statementKey}.");
        }

        var file = await LoadAsync(pseudonymId, db, protector, ct).ConfigureAwait(false);
        var text = StatementText(file, statementKey) ?? throw new DomainException(ErrorCodes.NotFound, $"Statement {statementKey} not in the file.");
        var stored = file.Summary?.Sources?.GetValueOrDefault(statementKey);
        var ids = EvidenceLinker.SourcesFor(text, stored, file.Transcript);
        var byId = file.Transcript.ToDictionary(t => t.Id);
        var present = ids.Where(byId.ContainsKey).ToList();
        audit.Append("patient.evidence_viewed", "TriageCase", file.Triage.Id.ToString(), file.PseudonymId, statementKey);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return TypedResults.Ok(new EvidenceDto(statementKey, present, present.Select(i => byId[i].Turn).ToList(), Removed: present.Count == 0 && (file.Transcript.Count == 0 || stored is { Count: > 0 })));
    }

    /// <summary>SB-15: approve as is or with edits. Needs the version the therapist read (If-Match); 412 when it changed.</summary>
    internal static async Task<Ok<PatientFileDto>> ApproveAsync(string pseudonymId, ApproveFileRequest request, HttpContext http, ICurrentUser user, TamaneenDbContext db, IPatientDataProtector protector, QuestionnaireCatalog catalog, IAuditLog audit, IClock clock, CancellationToken ct)
    {
        var file = await LoadAsync(pseudonymId, db, protector, ct).ConfigureAwait(false);
        EnsureVersion(http, file);
        EnsureNotApproved(file);
        var edits = (request.Edits ?? []).ToDictionary(kv => kv.Key, kv => kv.Value?.Trim() ?? string.Empty, StringComparer.Ordinal);
        if (edits.Any(kv => kv.Key == Hypotheses || !StatementKeys.Contains(kv.Key) || kv.Value.Length is 0 or > 2000))
        {
            throw MeEndpoints.Invalid("edits");
        }

        var review = file.Review ?? AddReview(db, file, clock);
        if (edits.Count > 0)
        {
            var merged = await EditsAsync(file.PatientId, review, protector, ct).ConfigureAwait(false);
            foreach (var (key, value) in edits)
            {
                merged[key] = value;
            }

            review.SaveEdits(await protector.EncryptAsync(file.PatientId, Fields.FileEdits, JsonSerializer.Serialize(merged, PatientContext.Json), ct).ConfigureAwait(false), clock);
        }

        if (file.Intake is { Status: IntakeStatus.Submitted } intake)
        {
            var tracked = await db.IntakeSessions.FirstAsync(i => i.Id == intake.Id, ct).ConfigureAwait(false);
            tracked.ApproveByTherapist(user.UserId!.Value, clock);
        }
        else
        {
            file.Triage.ApproveByTherapist(user.UserId!.Value, clock);
        }

        audit.Append("patient.file_approved", "UserAccount", file.PatientId.ToString(), file.PseudonymId, edits.Count == 0 ? "as_is" : $"edits={string.Join(',', edits.Keys)}");
        await SaveAsync(db, ct).ConfigureAwait(false);
        return await ReloadAsync(pseudonymId, http, user, db, protector, catalog, clock, ct).ConfigureAwait(false);
    }

    /// <summary>SB-14: hides a statement from the file; the reason stays in the audit log.</summary>
    internal static async Task<Ok<PatientFileDto>> DismissAsync(string pseudonymId, DismissRequest request, HttpContext http, ICurrentUser user, TamaneenDbContext db, IPatientDataProtector protector, QuestionnaireCatalog catalog, IAuditLog audit, IClock clock, CancellationToken ct)
    {
        var reason = request.Reason?.Trim();
        if (string.IsNullOrEmpty(reason))
        {
            throw new DomainException(ErrorCodes.ReasonRequired, "A reason is required to dismiss a statement.");
        }

        if (reason.Length > TriageCase.MaxReasonLength || request.StatementKey is not { } key || !StatementKeys.Contains(key))
        {
            throw MeEndpoints.Invalid("statementKey");
        }

        var file = await LoadAsync(pseudonymId, db, protector, ct).ConfigureAwait(false);
        EnsureNotApproved(file);
        if (StatementText(file, key) is null)
        {
            throw new DomainException(ErrorCodes.NotFound, $"Statement {key} not in the file.");
        }

        (file.Review ?? AddReview(db, file, clock)).Dismiss(key, clock);
        audit.Append("patient.file_item_dismissed", "TriageCase", file.Triage.Id.ToString(), file.PseudonymId, Truncate($"{key}: {reason}"));
        await SaveAsync(db, ct).ConfigureAwait(false);
        return await ReloadAsync(pseudonymId, http, user, db, protector, catalog, clock, ct).ConfigureAwait(false);
    }

    /// <summary>SB-16: how useful the file was, 1 to 5, with an optional comment (feeds the experience metric).</summary>
    internal static async Task<NoContent> FeedbackAsync(string pseudonymId, FileFeedbackRequest request, ICurrentUser user, TamaneenDbContext db, IPatientDataProtector protector, IAuditLog audit, IClock clock, CancellationToken ct)
    {
        var comment = request.Comment?.Trim();
        if (request.Rating is < 1 or > 5 || comment is { Length: > FileReview.MaxCommentLength })
        {
            throw MeEndpoints.Invalid("rating");
        }

        var file = await LoadAsync(pseudonymId, db, protector, ct).ConfigureAwait(false);
        var review = file.Review ?? AddReview(db, file, clock);
        var cipher = string.IsNullOrEmpty(comment) ? null : await protector.EncryptAsync(file.PatientId, Fields.FileFeedback, comment, ct).ConfigureAwait(false);
        review.Rate(user.UserId!.Value, request.Rating, cipher, clock);
        audit.Append("patient.file_rated", "TriageCase", file.Triage.Id.ToString(), file.PseudonymId, $"rating={request.Rating}");
        await SaveAsync(db, ct).ConfigureAwait(false);
        return TypedResults.NoContent();
    }

    /// <summary>SB-17: raise the risk level with a reason. Lowering is never possible here; it goes through a supervisor (S03).</summary>
    internal static async Task<Ok<RiskFlagResponse>> RiskFlagAsync(string pseudonymId, RiskFlagRequest request, TamaneenDbContext db, IAuditLog audit, IClock clock, CancellationToken ct)
    {
        var reason = request.Reason?.Trim();
        if (string.IsNullOrEmpty(reason))
        {
            throw new DomainException(ErrorCodes.ReasonRequired, "A reason is required to raise the risk level.");
        }

        if (reason.Length > TriageCase.MaxReasonLength || request.RiskLevel is < 1 or > 4)
        {
            throw MeEndpoints.Invalid("riskLevel");
        }

        var patientId = await StaffEndpoints.PatientIdAsync(db, pseudonymId, ct).ConfigureAwait(false);
        var triage = await db.TriageCases.Where(t => t.PatientId == patientId && t.Path != TriageCase.ChangePath).OrderByDescending(t => t.SubmittedAtUtc).FirstOrDefaultAsync(ct).ConfigureAwait(false)
            ?? throw new DomainException(ErrorCodes.NotFound, "No file for this patient.");
        var floor = await CrisisFloorAsync(db, patientId, clock, ct).ConfigureAwait(false);
        var current = (RiskLevel)Math.Max((int)triage.RiskLevel, (int)floor);
        if ((RiskLevel)request.RiskLevel <= current)
        {
            throw new DomainException(ErrorCodes.RiskNotHigher, $"Level {request.RiskLevel} is not above {current}.");
        }

        triage.RaiseRisk((RiskLevel)request.RiskLevel);
        audit.Append("patient.risk_raised", "TriageCase", triage.Id.ToString(), StaffEndpoints.Canonical(pseudonymId), Truncate($"{(int)current}->{request.RiskLevel}: {reason}"));
        await SaveAsync(db, ct).ConfigureAwait(false);
        return TypedResults.Ok(new RiskFlagResponse(request.RiskLevel));
    }

    /// <summary>
    /// SB-18: a manual crisis alert (level 3 or 4) with a note. It goes through the crisis worker like any other trigger,
    /// so the event opens (or is raised), the chat locks and the on-call clinician is paged; the note is the first entry
    /// on the event's timeline.
    /// </summary>
    internal static async Task<Ok<ManualCrisisResponse>> ManualCrisisAsync(string pseudonymId, ManualCrisisRequest request, ICurrentUser user, TamaneenDbContext db, CrisisGateClient gate, IPatientDataProtector protector, IAuditLog audit, IClock clock, CancellationToken ct)
    {
        var note = request.Note?.Trim();
        if (string.IsNullOrEmpty(note))
        {
            throw new DomainException(ErrorCodes.ReasonRequired, "A note is required to raise a crisis.");
        }

        if (note.Length > 2000 || request.Level is not ((int)RiskLevel.High or (int)RiskLevel.Critical))
        {
            throw MeEndpoints.Invalid("level");
        }

        var patientId = await StaffEndpoints.PatientIdAsync(db, pseudonymId, ct).ConfigureAwait(false);
        var language = await db.UserAccounts.AsNoTracking().Where(a => a.Id == patientId).Select(a => a.PreferredLanguage).FirstAsync(ct).ConfigureAwait(false);
        var verdict = await gate.RaiseManualAsync(patientId, language, (RiskLevel)request.Level, ct).ConfigureAwait(false);
        if (verdict.Unavailable || verdict.CrisisEventId is not { } eventId)
        {
            throw new DomainException(ErrorCodes.CrisisServiceUnavailable, "Crisis worker unavailable for a manual alert.");
        }

        var evt = await db.CrisisEvents.FirstOrDefaultAsync(e => e.Id == eventId, ct).ConfigureAwait(false);
        if (evt is not null && evt.Status != CrisisEventStatus.Closed)
        {
            db.CrisisActions.Add(CrisisAction.Record(evt, "note", user.UserId!.Value, user.Role!, await protector.EncryptAsync(patientId, Fields.CrisisNote, note, ct).ConfigureAwait(false), clock));
        }

        var triage = await db.TriageCases.Where(t => t.PatientId == patientId && t.Path != TriageCase.ChangePath).OrderByDescending(t => t.SubmittedAtUtc).FirstOrDefaultAsync(ct).ConfigureAwait(false);
        triage?.RaiseRisk(verdict.Level);
        audit.Append("crisis.manual_raised", "CrisisEvent", eventId.ToString(), StaffEndpoints.Canonical(pseudonymId), $"level={request.Level}");
        await SaveAsync(db, ct).ConfigureAwait(false);
        return TypedResults.Ok(new ManualCrisisResponse(eventId, (int)verdict.Level));
    }

    // ---------- helpers ----------

    /// <summary>Highest level of the patient's open crisis events or those opened in the last 30 days; risk never goes below it.</summary>
    internal static async Task<RiskLevel> CrisisFloorAsync(TamaneenDbContext db, Guid patientId, IClock clock, CancellationToken ct) =>
        (await CrisisFloorsAsync(db, [patientId], clock, ct).ConfigureAwait(false)).GetValueOrDefault(patientId);

    internal static async Task<Dictionary<Guid, RiskLevel>> CrisisFloorsAsync(TamaneenDbContext db, IReadOnlyCollection<Guid> patientIds, IClock clock, CancellationToken ct)
    {
        var since = clock.UtcNow.AddDays(-30);
        return (await db.CrisisEvents.AsNoTracking().Where(e => patientIds.Contains(e.PatientId) && (e.Status != CrisisEventStatus.Closed || e.OpenedAtUtc >= since))
            .Select(e => new { e.PatientId, e.Level }).ToListAsync(ct).ConfigureAwait(false))
            .GroupBy(e => e.PatientId).ToDictionary(g => g.Key, g => g.Max(e => e.Level));
    }

    internal static string Truncate(string value) => value.Length <= 1000 ? value : value[..1000];

    private static async Task<FileContext> LoadAsync(string pseudonymId, TamaneenDbContext db, IPatientDataProtector protector, CancellationToken ct)
    {
        var patientId = await StaffEndpoints.PatientIdAsync(db, pseudonymId, ct).ConfigureAwait(false);
        var triage = await db.TriageCases.Where(t => t.PatientId == patientId && t.Path != TriageCase.ChangePath).OrderByDescending(t => t.SubmittedAtUtc).FirstOrDefaultAsync(ct).ConfigureAwait(false)
            ?? throw new DomainException(ErrorCodes.NotFound, "No file for this patient.");
        var intake = triage.IntakeId is { } iid ? await db.IntakeSessions.AsNoTracking().FirstOrDefaultAsync(i => i.Id == iid, ct).ConfigureAwait(false) : null;
        IntakeEndpoints.StoredSummary? summary = null;
        if (intake?.SummaryCipher is { } cipher)
        {
            summary = JsonSerializer.Deserialize<IntakeEndpoints.StoredSummary>(await protector.DecryptAsync(patientId, Fields.IntakeSummary, cipher, ct).ConfigureAwait(false), PatientContext.Json);
        }

        var review = await db.FileReviews.FirstOrDefaultAsync(r => r.TriageCaseId == triage.Id, ct).ConfigureAwait(false);
        var transcript = intake is null ? [] : await IntakeEndpoints.TranscriptAsync(db, (field, c, token) => protector.DecryptAsync(patientId, field, c, token), intake.Id, ct).ConfigureAwait(false);
        return new FileContext(patientId, StaffEndpoints.Canonical(pseudonymId), triage, intake, summary, review, transcript);
    }

    private static async Task<Ok<PatientFileDto>> ReloadAsync(string pseudonymId, HttpContext http, ICurrentUser user, TamaneenDbContext db, IPatientDataProtector protector, QuestionnaireCatalog catalog, IClock clock, CancellationToken ct)
    {
        db.ChangeTracker.Clear();
        var dto = await BuildAsync(await LoadAsync(pseudonymId, db, protector, ct).ConfigureAwait(false), user, db, protector, catalog, clock, ct).ConfigureAwait(false);
        http.Response.Headers.ETag = $"\"{dto.Version}\"";
        return TypedResults.Ok(dto);
    }

    private static FileReview AddReview(TamaneenDbContext db, FileContext file, IClock clock)
    {
        var review = FileReview.Start(file.PatientId, file.Triage.Id, clock);
        db.FileReviews.Add(review);
        return review;
    }

    private static async Task SaveAsync(TamaneenDbContext db, CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new DomainException(ErrorCodes.FileVersionChanged, "The file changed concurrently.");
        }
    }

    private static bool IsApproved(FileContext file) =>
        file.Intake is not null ? file.Intake.TherapistApprovedAtUtc is not null : file.Triage.ReviewedAtUtc is not null;

    private static void EnsureNotApproved(FileContext file)
    {
        if (IsApproved(file))
        {
            throw new DomainException(ErrorCodes.FileApproved, "The file is already approved.");
        }
    }

    private static void EnsureVersion(HttpContext http, FileContext file)
    {
        var presented = http.Request.Headers.IfMatch.ToString().Trim();
        if (presented.Length == 0)
        {
            throw new DomainException(ErrorCodes.FileVersionRequired, "If-Match is required to approve a file.");
        }

        if (presented.Trim('"') != VersionOf(file))
        {
            throw new DomainException(ErrorCodes.FileVersionChanged, "The file changed since it was read.");
        }
    }

    /// <summary>Changes whenever something the therapist reads changes: the summary, an addendum, the level, an edit, a dismissal, the approval.</summary>
    private static string VersionOf(FileContext file)
    {
        var builder = new StringBuilder()
            .Append(file.Triage.Id).Append('|').Append((int)file.Triage.RiskLevel).Append('|').Append(file.Triage.ReviewedAtUtc?.ToUnixTimeMilliseconds())
            .Append('|').Append(Hash(file.Triage.AddendaCipher)).Append('|').Append(Hash(file.Intake?.SummaryCipher)).Append('|').Append(file.Intake?.TherapistApprovedAtUtc?.ToUnixTimeMilliseconds())
            .Append('|').Append(Hash(file.Review?.EditsCipher)).Append('|').Append(file.Review?.DismissedKeys);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString())))[..20].ToLowerInvariant();
    }

    private static string Hash(byte[]? bytes) => bytes is null ? "-" : Convert.ToHexString(SHA256.HashData(bytes))[..16];

    private static async Task<Dictionary<string, string>> EditsAsync(Guid patientId, FileReview? review, IPatientDataProtector protector, CancellationToken ct) =>
        review?.EditsCipher is { } cipher
            ? JsonSerializer.Deserialize<Dictionary<string, string>>(await protector.DecryptAsync(patientId, Fields.FileEdits, cipher, ct).ConfigureAwait(false), PatientContext.Json) ?? []
            : new Dictionary<string, string>(StringComparer.Ordinal);

    /// <summary>The AI (or patient-corrected) text of a statement, or null when the file has none.</summary>
    private static string? StatementText(FileContext file, string key)
    {
        if (key == Hypotheses)
        {
            return string.IsNullOrWhiteSpace(file.Summary?.Hypotheses) ? null : file.Summary.Hypotheses;
        }

        var text = file.Summary?.Sections.GetValueOrDefault(key)?.Text;
        return string.IsNullOrWhiteSpace(text) ? null : text;
    }

    private static async Task<PatientFileDto> BuildAsync(FileContext file, ICurrentUser user, TamaneenDbContext db, IPatientDataProtector protector, QuestionnaireCatalog catalog, IClock clock, CancellationToken ct)
    {
        var language = user.Language;
        var edits = await EditsAsync(file.PatientId, file.Review, protector, ct).ConfigureAwait(false);
        var dismissed = file.Review?.Dismissed ?? [];
        var statements = new List<StatementDto>();
        foreach (var key in StatementKeys)
        {
            if (StatementText(file, key) is not { } text || dismissed.Contains(key))
            {
                continue;
            }

            var section = key == Hypotheses ? null : file.Summary?.Sections.GetValueOrDefault(key);
            var sources = EvidenceLinker.SourcesFor(text, file.Summary?.Sources?.GetValueOrDefault(key), file.Transcript);
            var byPatient = section?.EditedByPatient ?? false;

            // EC-15: an AI statement with no source message is not shown. A patient's own correction always is.
            if (sources.Count == 0 && !byPatient && file.Transcript.Count > 0)
            {
                continue;
            }

            var edited = edits.TryGetValue(key, out var mine);
            statements.Add(new StatementDto(key, edited ? mine! : text, byPatient, edited, key == Hypotheses, sources.Count));
        }

        var rows = file.Intake is null ? [] : await db.IntakeMessages.AsNoTracking().Where(m => m.IntakeId == file.Intake.Id).Select(m => new { m.Id, m.CreatedAtUtc }).ToListAsync(ct).ConfigureAwait(false);
        var at = rows.ToDictionary(r => r.Id, r => r.CreatedAtUtc);
        var transcript = file.Transcript.Select(t => new TranscriptTurnDto(t.Id, t.Turn, t.FromPatient ? "patient" : "assistant", t.Text, at.GetValueOrDefault(t.Id))).ToList();

        var corrections = new List<CorrectionDto>();
        foreach (var (key, section) in file.Summary?.Sections ?? [])
        {
            if (key == "extra" ? section.Text.Length > 0 : section.EditedByPatient)
            {
                corrections.Add(new CorrectionDto(key, file.Summary!.AiDraft?.GetValueOrDefault(key), section.Text));
            }
        }

        var addenda = file.Triage.AddendaCipher is { } addendaCipher
            ? (await protector.DecryptAsync(file.PatientId, Fields.TriageAddenda, addendaCipher, ct).ConfigureAwait(false)).Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            : [];

        var screening = await db.Screenings.AsNoTracking().Where(s => s.PatientId == file.PatientId).OrderByDescending(s => s.CompletedAtUtc).FirstOrDefaultAsync(ct).ConfigureAwait(false);
        CssrsDto? cssrs = null;
        if (screening is not null)
        {
            var answers = await ScreeningAnswersAsync(file.PatientId, screening, protector, ct).ConfigureAwait(false);
            var count = catalog.ItemCount("C-SSRS-SCREEN");
            cssrs = new CssrsDto(screening.Positive, (int)screening.Level, screening.Version, screening.CompletedAtUtc,
                Enumerable.Range(1, count).Select(i => new CssrsItemDto(i, catalog.ItemText("C-SSRS-SCREEN", i, language), answers is not null && i <= answers.Count ? answers[i - 1] : null)).ToList());
        }

        var approvedAt = file.Intake is not null ? file.Intake.TherapistApprovedAtUtc : file.Triage.ReviewedAtUtc;
        var approvedBy = file.Intake is not null ? file.Intake.TherapistApprovedBy : file.Triage.ReviewedBy;
        var approverName = approvedBy is { } by ? await db.StaffProfiles.AsNoTracking().Where(s => s.UserId == by).Select(s => language == "en" ? s.DisplayNameEn : s.DisplayNameAr).FirstOrDefaultAsync(ct).ConfigureAwait(false) : null;
        var comment = file.Review?.RatingCommentCipher is { } commentCipher ? await protector.DecryptAsync(file.PatientId, Fields.FileFeedback, commentCipher, ct).ConfigureAwait(false) : null;
        var floor = await CrisisFloorAsync(db, file.PatientId, clock, ct).ConfigureAwait(false);
        var now = clock.UtcNow;
        var first = await db.Appointments.AsNoTracking().Where(a => a.PatientId == file.PatientId && a.Status == AppointmentStatus.Confirmed && a.StartsAtUtc > now)
            .OrderBy(a => a.StartsAtUtc).Select(a => (DateTimeOffset?)a.StartsAtUtc).FirstOrDefaultAsync(ct).ConfigureAwait(false);
        var approved = approvedAt is not null;

        return new PatientFileDto(
            file.PseudonymId,
            Math.Max((int)file.Triage.RiskLevel, (int)floor),
            (int)floor,
            first,
            file.Triage.Path,
            VersionOf(file),
            file.Intake?.SummaryByAi ?? false,
            statements,
            transcript,
            file.Intake is not null && transcript.Count == 0,
            file.Intake is null ? null : Instrument(file.Intake, "PHQ-9", catalog, language),
            file.Intake is null ? null : Instrument(file.Intake, "GAD-7", catalog, language),
            cssrs,
            corrections,
            addenda,
            new ApprovalDto(approved, approved ? approverName : null, approvedAt),
            new FileFeedbackDto(file.Review?.Rating, comment),
            user.Role == AppRoles.Therapist && !approved,
            file.Triage.SubmittedAtUtc);
    }

    private static async Task<List<bool>?> ScreeningAnswersAsync(Guid patientId, Screening screening, IPatientDataProtector protector, CancellationToken ct)
    {
        try
        {
            return JsonSerializer.Deserialize<List<bool>>(await protector.DecryptAsync(patientId, Fields.ScreeningAnswers, screening.AnswersCipher, ct).ConfigureAwait(false), PatientContext.Json);
        }
        catch (JsonException)
        {
            // Another answer format: the result is still shown, the items without answers.
            return null;
        }
    }

    private static InstrumentDto Instrument(IntakeSession intake, string instrument, QuestionnaireCatalog catalog, string language)
    {
        var scores = intake.Scores(instrument);
        var count = catalog.ItemCount(instrument);
        var total = intake.Total(instrument);
        var items = Enumerable.Range(1, count).Select(i => new ScoreItemDto(i, catalog.ItemText(instrument, i, language), scores.TryGetValue(i, out var v) ? v : null)).ToList();
        return new InstrumentDto(instrument, total, total is { } t ? Severity(instrument, t) : null, instrument == "PHQ-9" && scores.TryGetValue(9, out var nine) ? nine : null, catalog.Version, items);
    }

    /// <summary>Standard severity bands: PHQ-9 5/10/15/20, GAD-7 5/10/15.</summary>
    internal static string Severity(string instrument, int total) => instrument == "PHQ-9"
        ? total switch { < 5 => "minimal", < 10 => "mild", < 15 => "moderate", < 20 => "moderately_severe", _ => "severe" }
        : total switch { < 5 => "minimal", < 10 => "mild", < 15 => "moderate", _ => "severe" };
}
