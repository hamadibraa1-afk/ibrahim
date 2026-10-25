using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;
using Tamaneen.AiGateway.Gateway;
using Tamaneen.AiGateway.Model;
using Tamaneen.AiGateway.Prompts;
using Tamaneen.Api.Clinical;
using Tamaneen.Api.Gate;
using Tamaneen.Api.Patients;
using Tamaneen.Clinical.Intake;
using Tamaneen.Hosting;
using Tamaneen.Identity;
using Tamaneen.Infrastructure.Auditing;
using Tamaneen.SharedKernel;

namespace Tamaneen.Api.Endpoints;

/// <summary>
/// Intake (P04, P05, P36) and the direct-path safety check (P23). Every patient word and answer goes
/// through the crisis gate first; only then is it stored, and only then may the AI see it.
/// </summary>
public static class IntakeEndpoints
{
    public const int MaxMessageChars = 4000;
    private static readonly string[] SummarySections = ["concern", "goals", "supports", "priorCare", "extra"];

    public sealed record MessageDto(Guid Id, string Role, string Text, DateTimeOffset At, bool Fallback);

    public sealed record OptionDto(int Value, string Label);

    public sealed record PendingQuestionDto(string Instrument, int Item, int Total, string Text, IReadOnlyList<OptionDto> Options);

    public sealed record IntakeStateDto(Guid IntakeId, string Status, int Progress, IReadOnlyList<MessageDto> Messages, PendingQuestionDto? PendingQuestion, bool CanFinish, DateTimeOffset ExpiresAt, bool Crisis = false, string? Script = null);

    public sealed record SendMessageRequest(string? Text);

    public sealed record AnswerRequest(string? Instrument, int Item, int? Value);

    public sealed record SectionDto(string Text, bool EditedByPatient);

    public sealed record SummaryDto(Guid IntakeId, IReadOnlyDictionary<string, SectionDto> Sections, bool GeneratedByAi, bool Crisis = false, string? Script = null);

    public sealed record UpdateSummaryRequest(Dictionary<string, string>? Sections);

    /// <summary><paramref name="Referrals"/> lists the referral codes when a supervisor closed the case without matching (S03).</summary>
    public sealed record TriageStateDto(string Status, DateTimeOffset? SubmittedAt, DateTimeOffset? ExpectedBy, bool SuggestionsReady, bool Crisis = false, string? Script = null, IReadOnlyList<string>? Referrals = null);

    public sealed record AddendumRequest(string? Text);

    public sealed record ScreeningQuestionDto(int Index, string Text);

    public sealed record ScreeningFormDto(string Instrument, string Version, IReadOnlyList<ScreeningQuestionDto> Questions, string Note);

    public sealed record ScreeningSubmit(List<bool>? Answers);

    /// <summary>
    /// Stored (encrypted) summary: section text plus whether the patient changed it. For the therapist only (S02):
    /// the source messages of each section, the AI's hypotheses to explore, and the AI's wording of any section the
    /// patient corrected. The patient endpoints return <see cref="Sections"/> alone.
    /// </summary>
    public sealed record StoredSummary(Dictionary<string, SectionDto> Sections, Dictionary<string, List<Guid>>? Sources = null, string? Hypotheses = null, Dictionary<string, string>? AiDraft = null);

    public static IEndpointRouteBuilder MapIntakeEndpoints(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/v1").RequireAuthorization(AuthSetup.PatientPolicy);
        g.MapGet("/screening/cssrs", ScreeningForm);
        g.MapPost("/screening/cssrs", SubmitScreeningAsync);
        g.MapPost("/intake", StartAsync);
        g.MapGet("/intake/current", CurrentAsync);
        g.MapPost("/intake/{id:guid}/messages", SendAsync).RequireRateLimiting(ApiHost.PatientRateLimitPolicy);
        g.MapPost("/intake/{id:guid}/answers", AnswerAsync);
        g.MapPost("/intake/{id:guid}/pause", PauseAsync);
        g.MapPost("/intake/{id:guid}/complete", CompleteAsync).RequireRateLimiting(ApiHost.PatientRateLimitPolicy);
        g.MapGet("/intake/{id:guid}/summary", GetSummaryAsync);
        g.MapPut("/intake/{id:guid}/summary", UpdateSummaryAsync);
        g.MapPost("/intake/{id:guid}/submit", SubmitAsync);
        g.MapGet("/triage", TriageAsync);
        g.MapPost("/triage/addendum", AddendumAsync);
        return app;
    }

    // ---------- P23 safety check ----------

    internal static Ok<ScreeningFormDto> ScreeningForm(PatientContext p, QuestionnaireCatalog catalog, IStringLocalizer<SharedResource> text)
    {
        var count = catalog.ItemCount("C-SSRS-SCREEN");
        var questions = Enumerable.Range(1, count).Select(i => new ScreeningQuestionDto(i, catalog.ItemText("C-SSRS-SCREEN", i, p.Language))).ToList();
        var note = LocalizationSetup.WithLanguage(p.Language, () => text["screening.note"].Value);
        return TypedResults.Ok(new ScreeningFormDto("cssrs", catalog.Version, questions, note));
    }

    internal static async Task<Ok<GateOutcomeDto>> SubmitScreeningAsync(ScreeningSubmit request, PatientContext p, QuestionnaireCatalog catalog, IAuditLog audit, IClock clock, CancellationToken ct)
    {
        var count = catalog.ItemCount("C-SSRS-SCREEN");
        if (request.Answers is not { } answers || answers.Count != count)
        {
            throw MeEndpoints.Invalid("answers");
        }

        var scores = answers.Select((yes, i) => (yes, i)).ToDictionary(x => x.i + 1, x => x.yes ? 1 : 0);
        var verdict = await p.GateAsync(null, new GateAnswers("C-SSRS-SCREEN", scores), "screening", ct).ConfigureAwait(false);
        var positive = answers.Any(a => a);
        var level = verdict.Level < RiskLevel.Routine ? RiskLevel.Routine : verdict.Level;
        var screening = Screening.Record(p.PatientId, "C-SSRS-SCREEN", catalog.Version, positive, level, await p.EncryptJsonAsync(Fields.ScreeningAnswers, answers, ct).ConfigureAwait(false), clock);
        p.Db.Screenings.Add(screening);
        audit.Append("screening.completed", "Screening", screening.Id.ToString(), (await p.AccountAsync(ct).ConfigureAwait(false)).PseudonymId, $"positive={positive};level={(int)level}");
        await p.Db.SaveChangesAsync(ct).ConfigureAwait(false);
        return TypedResults.Ok(PatientContext.Outcome(verdict));
    }

    // ---------- P04 intake chat ----------

    internal static async Task<Ok<IntakeStateDto>> StartAsync(PatientContext p, QuestionnaireCatalog catalog, IStringLocalizer<SharedResource> text, IClock clock, CancellationToken ct)
    {
        var current = await LatestAsync(p, clock, ct).ConfigureAwait(false);
        if (current is { Status: IntakeStatus.Active or IntakeStatus.Paused or IntakeStatus.Review or IntakeStatus.Submitted })
        {
            return TypedResults.Ok(await StateAsync(p, current, catalog, ct).ConfigureAwait(false));
        }

        // No intake yet, or the last one expired: start a fresh conversation with the fixed greeting.
        var intake = IntakeSession.Start(p.PatientId, clock);
        p.Db.IntakeSessions.Add(intake);
        var greeting = LocalizationSetup.WithLanguage(p.Language, () => text["intake.greeting"].Value);
        p.Db.IntakeMessages.Add(IntakeMessage.Assistant(intake.Id, p.PatientId, await p.EncryptAsync(Fields.IntakeMessage, greeting, ct).ConfigureAwait(false), false, clock));
        await p.Db.SaveChangesAsync(ct).ConfigureAwait(false);
        return TypedResults.Ok(await StateAsync(p, intake, catalog, ct).ConfigureAwait(false));
    }

    internal static async Task<Ok<IntakeStateDto>> CurrentAsync(PatientContext p, QuestionnaireCatalog catalog, IClock clock, CancellationToken ct)
    {
        var current = await LatestAsync(p, clock, ct).ConfigureAwait(false) ?? throw new DomainException(ErrorCodes.NotFound, "No intake.");
        return TypedResults.Ok(await StateAsync(p, current, catalog, ct).ConfigureAwait(false));
    }

    internal static async Task<Ok<IntakeStateDto>> SendAsync(Guid id, SendMessageRequest request, PatientContext p, IAiGateway ai, PromptCatalog prompts, QuestionnaireCatalog catalog, IClock clock, CancellationToken ct)
    {
        var text = request.Text?.Trim();
        if (string.IsNullOrEmpty(text) || text.Length > MaxMessageChars)
        {
            throw new DomainException(ErrorCodes.AiMessageInvalid, "Intake message empty or too long.", MaxMessageChars);
        }

        var intake = await FindAsync(p, id, ct).ConfigureAwait(false);
        intake.RecordPatientTurn(clock);
        var verdict = await p.GateAsync(text, null, "intake", ct).ConfigureAwait(false);
        p.Db.IntakeMessages.Add(IntakeMessage.Patient(intake.Id, p.PatientId, await p.EncryptAsync(Fields.IntakeMessage, text, ct).ConfigureAwait(false), clock));
        if (verdict.Crisis)
        {
            await p.Db.SaveChangesAsync(ct).ConfigureAwait(false);
            return TypedResults.Ok(await StateAsync(p, intake, catalog, ct, verdict).ConfigureAwait(false));
        }

        var history = await HistoryAsync(p, intake.Id, ct).ConfigureAwait(false);
        var (reply, fallback) = await AssistantReplyAsync(p, ai, prompts, "intake", text, history, ct).ConfigureAwait(false);
        p.Db.IntakeMessages.Add(IntakeMessage.Assistant(intake.Id, p.PatientId, await p.EncryptAsync(Fields.IntakeMessage, reply, ct).ConfigureAwait(false), fallback, new LaterClock(clock)));
        await p.Db.SaveChangesAsync(ct).ConfigureAwait(false);
        return TypedResults.Ok(await StateAsync(p, intake, catalog, ct).ConfigureAwait(false));
    }

    internal static async Task<Ok<IntakeStateDto>> AnswerAsync(Guid id, AnswerRequest request, PatientContext p, QuestionnaireCatalog catalog, IClock clock, CancellationToken ct)
    {
        var instrument = request.Instrument switch
        {
            "phq9" => "PHQ-9",
            "gad7" => "GAD-7",
            _ => throw MeEndpoints.Invalid("instrument"),
        };
        var intake = await FindAsync(p, id, ct).ConfigureAwait(false);
        intake.Answer(instrument, request.Item, request.Value, clock);
        GateVerdict? verdict = null;
        if (request.Value is not null)
        {
            // The whole instrument so far, so total-score rules apply once it is complete.
            verdict = await p.GateAsync(null, new GateAnswers(instrument, intake.Scores(instrument)), "questionnaire", ct).ConfigureAwait(false);
        }

        await p.Db.SaveChangesAsync(ct).ConfigureAwait(false);
        return TypedResults.Ok(await StateAsync(p, intake, catalog, ct, verdict is { Crisis: true } ? verdict : null).ConfigureAwait(false));
    }

    internal static async Task<Ok<IntakeStateDto>> PauseAsync(Guid id, PatientContext p, QuestionnaireCatalog catalog, IClock clock, CancellationToken ct)
    {
        var intake = await FindAsync(p, id, ct).ConfigureAwait(false);
        intake.Pause(clock);
        await p.Db.SaveChangesAsync(ct).ConfigureAwait(false);
        return TypedResults.Ok(await StateAsync(p, intake, catalog, ct).ConfigureAwait(false));
    }

    // ---------- P05 summary ----------

    internal static async Task<Ok<SummaryDto>> CompleteAsync(Guid id, PatientContext p, IAiGateway ai, IAuditLog audit, IClock clock, CancellationToken ct)
    {
        var intake = await FindAsync(p, id, ct).ConfigureAwait(false);
        await p.EnsureNotLockedAsync(ct).ConfigureAwait(false);
        var turns = await TranscriptAsync(p.Db, p.DecryptAsync, intake.Id, ct).ConfigureAwait(false);
        var transcript = EvidenceLinker.NumberedTranscript(turns);
        var sections = SummarySections.ToDictionary(s => s, _ => new SectionDto(string.Empty, false));
        var sources = new Dictionary<string, List<Guid>>(StringComparer.Ordinal);
        string? hypotheses = null;
        var byAi = false;
        if (await p.HasConsentAsync(ConsentCatalog.Ai, ct).ConfigureAwait(false) && transcript.Length > 0)
        {
            var pseudonym = (await p.AccountAsync(ct).ConfigureAwait(false)).PseudonymId;
            var known = await p.KnownIdentifiersAsync(ct).ConfigureAwait(false);
            var response = await ai.AskAsync(new AiRequest(pseudonym, "intake-summary", p.Language, Truncate(transcript, 15000), [], known), ct).ConfigureAwait(false);
            var parts = response.IsFallback ? [] : response.Text.Split("\n---", StringSplitOptions.TrimEntries).Select(s => s.Trim('-', ' ', '\n', '\r')).Where(s => s.Length > 0).ToList();
            if (parts.Count == 4)
            {
                string[] keys = ["concern", "goals", "supports", "priorCare"];
                for (var i = 0; i < keys.Length; i++)
                {
                    // The citation is for the therapist; the patient reads the paragraph without it.
                    var (text, cited) = EvidenceLinker.SplitCitation(parts[i]);
                    sections[keys[i]] = new SectionDto(text, false);
                    sources[keys[i]] = EvidenceLinker.Resolve(cited, turns);
                }

                byAi = true;

                // Hypotheses to explore, for the therapist only and never shown to the patient (S02).
                var draft = await ai.AskAsync(new AiRequest(pseudonym, "intake-hypotheses", p.Language, Truncate(transcript, 15000), [], known), ct).ConfigureAwait(false);
                if (!draft.IsFallback)
                {
                    var (text, cited) = EvidenceLinker.SplitCitation(draft.Text);
                    hypotheses = text;
                    sources["hypotheses"] = EvidenceLinker.Resolve(cited, turns);
                }
            }
        }

        intake.Complete(await p.EncryptJsonAsync(Fields.IntakeSummary, new StoredSummary(sections, sources, hypotheses), ct).ConfigureAwait(false), byAi, clock);
        audit.Append("intake.summary_generated", "IntakeSession", intake.Id.ToString(), (await p.AccountAsync(ct).ConfigureAwait(false)).PseudonymId, byAi ? "ai" : "manual");
        await p.Db.SaveChangesAsync(ct).ConfigureAwait(false);
        return TypedResults.Ok(new SummaryDto(intake.Id, sections, byAi));
    }

    internal static async Task<Ok<SummaryDto>> GetSummaryAsync(Guid id, PatientContext p, CancellationToken ct)
    {
        var intake = await FindAsync(p, id, ct).ConfigureAwait(false);
        var stored = await p.DecryptJsonAsync<StoredSummary>(Fields.IntakeSummary, intake.SummaryCipher, ct).ConfigureAwait(false)
            ?? throw new DomainException(ErrorCodes.IntakeNotInReview, "No summary yet.");
        return TypedResults.Ok(new SummaryDto(intake.Id, stored.Sections, intake.SummaryByAi));
    }

    internal static async Task<Ok<SummaryDto>> UpdateSummaryAsync(Guid id, UpdateSummaryRequest request, PatientContext p, IClock clock, CancellationToken ct)
    {
        var intake = await FindAsync(p, id, ct).ConfigureAwait(false);
        var stored = await p.DecryptJsonAsync<StoredSummary>(Fields.IntakeSummary, intake.SummaryCipher, ct).ConfigureAwait(false)
            ?? throw new DomainException(ErrorCodes.IntakeNotInReview, "No summary yet.");
        var changes = (request.Sections ?? []).Where(kv => SummarySections.Contains(kv.Key)).ToList();
        if (changes.Any(kv => kv.Value is null || kv.Value.Length > 2000))
        {
            throw MeEndpoints.Invalid("sections");
        }

        var newText = string.Join("\n", changes.Select(kv => kv.Value.Trim()).Where(v => v.Length > 0));
        GateVerdict? verdict = null;
        if (newText.Length > 0)
        {
            verdict = await p.GateAsync(newText, null, "summary", ct).ConfigureAwait(false);
        }

        // The therapist sees what the AI wrote before the patient corrected it (S02 "patient corrections").
        var draftText = stored.AiDraft is null ? new Dictionary<string, string>(StringComparer.Ordinal) : new Dictionary<string, string>(stored.AiDraft, StringComparer.Ordinal);
        foreach (var (key, value) in changes)
        {
            if (stored.Sections.TryGetValue(key, out var before) && !before.EditedByPatient && !draftText.ContainsKey(key) && before.Text.Length > 0)
            {
                draftText[key] = before.Text;
            }

            stored.Sections[key] = new SectionDto(value.Trim(), true);
        }

        stored = stored with { AiDraft = draftText };
        intake.UpdateSummary(await p.EncryptJsonAsync(Fields.IntakeSummary, stored, ct).ConfigureAwait(false), clock);
        await p.Db.SaveChangesAsync(ct).ConfigureAwait(false);
        return TypedResults.Ok(new SummaryDto(intake.Id, stored.Sections, intake.SummaryByAi, verdict?.Crisis ?? false, verdict is { Crisis: true } ? verdict.Script : null));
    }

    internal static async Task<Ok<TriageStateDto>> SubmitAsync(Guid id, PatientContext p, IAuditLog audit, IClock clock, CancellationToken ct)
    {
        var intake = await FindAsync(p, id, ct).ConfigureAwait(false);
        await p.EnsureNotLockedAsync(ct).ConfigureAwait(false);
        intake.Submit(clock);
        var phq = intake.Total("PHQ-9");
        var gad = intake.Total("GAD-7");
        var hadCrisis = await p.Db.CrisisEvents.AnyAsync(e => e.PatientId == p.PatientId && e.OpenedAtUtc >= intake.StartedAtUtc, ct).ConfigureAwait(false);
        var risk = hadCrisis ? RiskLevel.High : phq >= 15 || gad >= 15 ? RiskLevel.Elevated : RiskLevel.Routine;
        var triage = TriageCase.FromIntake(p.PatientId, intake.Id, risk, phq, gad, clock);
        p.Db.TriageCases.Add(triage);
        audit.Append("intake.submitted", "TriageCase", triage.Id.ToString(), (await p.AccountAsync(ct).ConfigureAwait(false)).PseudonymId, $"risk={(int)risk}");
        await p.Db.SaveChangesAsync(ct).ConfigureAwait(false);
        return TypedResults.Ok(await TriageStateAsync(p, ct).ConfigureAwait(false));
    }

    // ---------- P36 waiting ----------

    internal static async Task<Ok<TriageStateDto>> TriageAsync(PatientContext p, CancellationToken ct) => TypedResults.Ok(await TriageStateAsync(p, ct).ConfigureAwait(false));

    internal static async Task<TriageStateDto> TriageStateAsync(PatientContext p, CancellationToken ct)
    {
        var triage = await p.Db.TriageCases.AsNoTracking().Where(t => t.PatientId == p.PatientId).OrderByDescending(t => t.SubmittedAtUtc).FirstOrDefaultAsync(ct).ConfigureAwait(false);
        if (triage is null)
        {
            return new TriageStateDto("none", null, null, false);
        }

        var ready = triage.Status is TriageStatus.Reviewed or TriageStatus.Matched && await p.Db.MatchSuggestions.AnyAsync(m => m.PatientId == p.PatientId && !m.Dismissed, ct).ConfigureAwait(false);
        var referrals = triage.Status == TriageStatus.Closed ? (triage.Referrals ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries) : null;
        return new TriageStateDto(MeEndpoints.TriageStatusCode(triage.Status), triage.SubmittedAtUtc, triage.DueAtUtc, ready, Referrals: referrals);
    }

    internal static async Task<Ok<TriageStateDto>> AddendumAsync(AddendumRequest request, PatientContext p, IAuditLog audit, CancellationToken ct)
    {
        var text = request.Text?.Trim();
        if (string.IsNullOrEmpty(text) || text.Length > 2000)
        {
            throw MeEndpoints.Invalid("text");
        }

        var triage = await p.Db.TriageCases.Where(t => t.PatientId == p.PatientId).OrderByDescending(t => t.SubmittedAtUtc).FirstOrDefaultAsync(ct).ConfigureAwait(false)
            ?? throw new DomainException(ErrorCodes.NotFound, "No triage case.");
        var verdict = await p.GateAsync(text, null, "triage", ct).ConfigureAwait(false);
        var existing = await p.DecryptOptionalAsync(Fields.TriageAddenda, triage.AddendaCipher, ct).ConfigureAwait(false);
        var combined = string.IsNullOrEmpty(existing) ? text : existing + "\n" + text;
        if (combined.Length > 6000)
        {
            throw MeEndpoints.Invalid("text");
        }

        triage.SetAddenda(await p.EncryptAsync(Fields.TriageAddenda, combined, ct).ConfigureAwait(false));
        triage.RaiseRisk(verdict.Level);
        audit.Append("triage.addendum_added", "TriageCase", triage.Id.ToString(), (await p.AccountAsync(ct).ConfigureAwait(false)).PseudonymId);
        await p.Db.SaveChangesAsync(ct).ConfigureAwait(false);
        return TypedResults.Ok(await TriageStateAsync(p, ct).ConfigureAwait(false) with { Crisis = verdict.Crisis, Script = verdict.Crisis ? verdict.Script : null });
    }

    // ---------- helpers ----------

    /// <summary>The AI reply, or a scripted follow-up when the patient withdrew AI consent.</summary>
    internal static async Task<(string Text, bool Fallback)> AssistantReplyAsync(PatientContext p, IAiGateway ai, PromptCatalog prompts, string purpose, string message, List<ChatTurn> history, CancellationToken ct)
    {
        if (!await p.HasConsentAsync(ConsentCatalog.Ai, ct).ConfigureAwait(false))
        {
            return (prompts.Get(purpose).For(p.Language).Fallback, true);
        }

        // The latest patient message is the request; earlier turns are history (both de-identified by the gateway).
        var earlier = history.Count > 0 && history[^1].Role == ChatRole.User ? history[..^1] : history;
        var account = await p.AccountAsync(ct).ConfigureAwait(false);
        var response = await ai.AskAsync(new AiRequest(account.PseudonymId, purpose, p.Language, message, earlier, await p.KnownIdentifiersAsync(ct).ConfigureAwait(false)), ct).ConfigureAwait(false);
        return (response.Text, response.IsFallback);
    }

    private static async Task<List<ChatTurn>> HistoryAsync(PatientContext p, Guid intakeId, CancellationToken ct)
    {
        var rows = await p.Db.IntakeMessages.AsNoTracking().Where(m => m.IntakeId == intakeId).OrderBy(m => m.CreatedAtUtc).ToListAsync(ct).ConfigureAwait(false);
        var tracked = p.Db.ChangeTracker.Entries<IntakeMessage>().Where(e => e.State == EntityState.Added && e.Entity.IntakeId == intakeId).Select(e => e.Entity);
        var turns = new List<ChatTurn>();
        foreach (var m in rows.Concat(tracked).OrderBy(m => m.CreatedAtUtc))
        {
            turns.Add(new ChatTurn(m.FromPatient ? ChatRole.User : ChatRole.Assistant, await p.DecryptAsync(Fields.IntakeMessage, m.TextCipher, ct).ConfigureAwait(false)));
        }

        return turns;
    }

    /// <summary>Every turn of an intake in order, numbered from 1, decrypted with the patient's key.</summary>
    internal static async Task<List<TranscriptTurn>> TranscriptAsync(Tamaneen.Infrastructure.Persistence.TamaneenDbContext db, Func<string, byte[], CancellationToken, Task<string>> decrypt, Guid intakeId, CancellationToken ct)
    {
        var rows = await db.IntakeMessages.AsNoTracking().Where(m => m.IntakeId == intakeId).OrderBy(m => m.CreatedAtUtc).ToListAsync(ct).ConfigureAwait(false);
        var turns = new List<TranscriptTurn>(rows.Count);
        for (var i = 0; i < rows.Count; i++)
        {
            turns.Add(new TranscriptTurn(rows[i].Id, i + 1, rows[i].FromPatient, await decrypt(Fields.IntakeMessage, rows[i].TextCipher, ct).ConfigureAwait(false)));
        }

        return turns;
    }

    private static async Task<IntakeSession?> LatestAsync(PatientContext p, IClock clock, CancellationToken ct)
    {
        var intake = await p.Db.IntakeSessions.Where(i => i.PatientId == p.PatientId).OrderByDescending(i => i.StartedAtUtc).FirstOrDefaultAsync(ct).ConfigureAwait(false);
        if (intake is not null && intake.ExpireIfStale(clock))
        {
            await p.Db.SaveChangesAsync(ct).ConfigureAwait(false);
        }

        return intake;
    }

    private static async Task<IntakeSession> FindAsync(PatientContext p, Guid id, CancellationToken ct) =>
        await p.Db.IntakeSessions.FirstOrDefaultAsync(i => i.Id == id && i.PatientId == p.PatientId, ct).ConfigureAwait(false)
            ?? throw new DomainException(ErrorCodes.NotFound, $"Intake {id} not found.");

    private static async Task<IntakeStateDto> StateAsync(PatientContext p, IntakeSession intake, QuestionnaireCatalog catalog, CancellationToken ct, GateVerdict? crisis = null)
    {
        var rows = await p.Db.IntakeMessages.AsNoTracking().Where(m => m.IntakeId == intake.Id).OrderBy(m => m.CreatedAtUtc).ToListAsync(ct).ConfigureAwait(false);
        var messages = new List<MessageDto>(rows.Count);
        foreach (var m in rows)
        {
            messages.Add(new MessageDto(m.Id, m.FromPatient ? "patient" : "assistant", await p.DecryptAsync(Fields.IntakeMessage, m.TextCipher, ct).ConfigureAwait(false), m.CreatedAtUtc, m.IsFallback));
        }

        PendingQuestionDto? pending = null;
        if (intake.PendingQuestion() is { } q)
        {
            var text = $"{catalog.Stem(q.Instrument, p.Language)} {catalog.ItemText(q.Instrument, q.Item, p.Language)}";
            pending = new PendingQuestionDto(q.Instrument == "PHQ-9" ? "phq9" : "gad7", q.Item, q.Total, text, catalog.Options(q.Instrument, p.Language).Select(o => new OptionDto(o.Value, o.Label)).ToList());
        }

        return new IntakeStateDto(
            intake.Id,
            MeEndpoints.IntakeStatusCode(intake.Status),
            intake.Progress,
            messages,
            crisis is null ? pending : null,
            intake.CanFinish,
            intake.ExpiresAtUtc,
            crisis is not null,
            crisis?.Script);
    }

    private static string Truncate(string value, int max) => value.Length <= max ? value : value[^max..];

    /// <summary>Gives the assistant's turn a timestamp after the patient's so ordering is stable.</summary>
    private sealed class LaterClock(IClock inner) : IClock
    {
        public DateTimeOffset UtcNow { get; } = inner.UtcNow.AddMilliseconds(1);
    }
}
