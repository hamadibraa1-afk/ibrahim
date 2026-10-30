using System.Text.Json;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Tamaneen.Api.Clinical;
using Tamaneen.Api.Notifications;
using Tamaneen.Api.Patients;
using Tamaneen.Clinical.Care;
using Tamaneen.Infrastructure.Auditing;
using Tamaneen.Infrastructure.Security;
using Tamaneen.SharedKernel;

namespace Tamaneen.Api.Endpoints;

/// <summary>S16: the therapist's view of a patient's homework, the library, assign, edit, cancel and reply to a check-in.</summary>
public static partial class StaffCareEndpoints
{
    public const int MaxHomeworkTitle = 120;
    public const int MaxHomeworkInstructions = 2000;

    public sealed record HomeworkTemplateDto(string Code, string Title, IReadOnlyList<string> Instructions, string Frequency, int? Minutes);

    /// <summary><c>StartsOn</c> is ignored: the exercise starts on the patient's today, in their time zone.</summary>
    public sealed record AssignHomeworkRequest(string? Title, List<string>? Instructions, int? Minutes, string? Frequency, DateOnly? StartsOn, DateOnly EndsOn, string? TemplateCode = null);

    public sealed record UpdateHomeworkRequest(string? Title, List<string>? Instructions, int? Minutes, string? Frequency, DateOnly EndsOn);

    public sealed record FeedbackRequest(string? Text);

    public sealed record StaffCheckinDto(Guid Id, DateOnly Day, string Status, int? Feeling, string? Note, int? GateLevel, string? Feedback, DateTimeOffset? FeedbackAt, bool FeedbackSeen);

    public sealed record StaffHomeworkDto(
        Guid Id,
        string Title,
        IReadOnlyList<string> Instructions,
        int? Minutes,
        string Frequency,
        DateOnly StartsOn,
        DateOnly EndsOn,
        string Status,
        string? TemplateCode,
        int DoneThisWeek,
        int WeeklyTarget,
        double? AverageFeeling,
        StaffCheckinDto? LatestCheckin,
        IReadOnlyList<StaffCheckinDto> Checkins);

    public sealed record StaffHomeworkListDto(string PseudonymId, IReadOnlyList<StaffHomeworkDto> Current, IReadOnlyList<StaffHomeworkDto> Past, bool CanEdit);

    internal static Ok<List<HomeworkTemplateDto>> HomeworkTemplatesAsync(ICurrentUser user, HomeworkTemplates templates) =>
        TypedResults.Ok(templates.Items.Select(t => new HomeworkTemplateDto(
            t.Code,
            user.Language == "en" ? t.Title.En : t.Title.Ar,
            user.Language == "en" ? t.Instructions.En : t.Instructions.Ar,
            t.Frequency,
            t.Minutes)).ToList());

    /// <summary>Every assignment with this week's progress, the patient's notes and the replies. Audited.</summary>
    internal static async Task<Ok<StaffHomeworkListDto>> HomeworkAsync(string pseudonymId, ICurrentUser user, CareRecords care, IPatientDataProtector protector, IAuditLog audit, IClock clock, CancellationToken ct)
    {
        var patientId = await StaffEndpoints.PatientIdAsync(care.Db, pseudonymId, ct).ConfigureAwait(false);
        var today = await care.TodayAsync(patientId, clock, ct).ConfigureAwait(false);
        var items = await HomeworkRowsAsync(care, protector, patientId, today, null, ct).ConfigureAwait(false);
        audit.Append("patient.homework_viewed", "UserAccount", patientId.ToString(), StaffEndpoints.Canonical(pseudonymId));
        await care.Db.SaveChangesAsync(ct).ConfigureAwait(false);
        return TypedResults.Ok(new StaffHomeworkListDto(
            StaffEndpoints.Canonical(pseudonymId),
            items.Where(i => i.Status == "active").ToList(),
            items.Where(i => i.Status != "active").ToList(),
            user.Role == AppRoles.Therapist));
    }

    /// <summary>SB-21: assign from the library or with the therapist's own text; schedule and end date required. The patient is notified.</summary>
    internal static async Task<Created<StaffHomeworkDto>> AssignHomeworkAsync(string pseudonymId, AssignHomeworkRequest request, ICurrentUser user, CareRecords care, HomeworkTemplates templates, IPatientDataProtector protector, IAuditLog audit, IClock clock, CancellationToken ct)
    {
        CareRecords.RequireTherapist(user);
        var (title, instructions) = CleanHomework(request.Title, request.Instructions);
        var frequency = ParseFrequency(request.Frequency);
        if (request.TemplateCode is { } code && !templates.Contains(code))
        {
            throw MeEndpoints.Invalid("templateCode");
        }

        var patientId = await StaffEndpoints.PatientIdAsync(care.Db, pseudonymId, ct).ConfigureAwait(false);
        var today = await care.TodayAsync(patientId, clock, ct).ConfigureAwait(false);
        if (request.EndsOn < today)
        {
            throw MeEndpoints.Invalid("endsOn");
        }

        var assignment = HomeworkAssignment.Assign(
            patientId,
            user.UserId!.Value,
            await protector.EncryptAsync(patientId, Fields.HomeworkTitle, title, ct).ConfigureAwait(false),
            await protector.EncryptAsync(patientId, Fields.HomeworkInstructions, CareRecords.Json(instructions), ct).ConfigureAwait(false),
            request.Minutes is > 0 and <= 240 ? request.Minutes : null,
            frequency,
            today,
            request.EndsOn,
            clock);
        assignment.SetTemplate(request.TemplateCode);
        care.Db.HomeworkAssignments.Add(assignment);
        audit.Append("homework.assigned", "HomeworkAssignment", assignment.Id.ToString(), StaffEndpoints.Canonical(pseudonymId));
        await care.Db.SaveChangesAsync(ct).ConfigureAwait(false);
        await care.NotifyAsync(patientId, "homework.assigned", new Dictionary<string, string>
        {
            ["homeworkId"] = assignment.Id.ToString(),
            ["therapistName"] = await care.StaffNameAsync(user.UserId, await PatientLanguageAsync(care, patientId, ct).ConfigureAwait(false), ct).ConfigureAwait(false),
        }, $"/homework/{assignment.Id}", ct).ConfigureAwait(false);
        var dto = (await HomeworkRowsAsync(care, protector, patientId, today, assignment.Id, ct).ConfigureAwait(false)).Single();
        return TypedResults.Created($"/v1/homework/{assignment.Id}", dto);
    }

    /// <summary>SB-22 edit: the patient sees the new text at once.</summary>
    internal static async Task<Ok<StaffHomeworkDto>> UpdateHomeworkAsync(Guid id, UpdateHomeworkRequest request, ICurrentUser user, CareRecords care, IPatientDataProtector protector, IAuditLog audit, IClock clock, CancellationToken ct)
    {
        CareRecords.RequireTherapist(user);
        var (title, instructions) = CleanHomework(request.Title, request.Instructions);
        var frequency = ParseFrequency(request.Frequency);
        var assignment = await care.Db.HomeworkAssignments.FirstOrDefaultAsync(a => a.Id == id, ct).ConfigureAwait(false)
            ?? throw new DomainException(ErrorCodes.NotFound, $"Homework {id} not found.");
        var today = await care.TodayAsync(assignment.PatientId, clock, ct).ConfigureAwait(false);
        assignment.Update(
            await protector.EncryptAsync(assignment.PatientId, Fields.HomeworkTitle, title, ct).ConfigureAwait(false),
            await protector.EncryptAsync(assignment.PatientId, Fields.HomeworkInstructions, CareRecords.Json(instructions), ct).ConfigureAwait(false),
            request.Minutes is > 0 and <= 240 ? request.Minutes : null,
            frequency,
            request.EndsOn,
            today,
            clock);
        audit.Append("homework.updated", "HomeworkAssignment", assignment.Id.ToString(), await PseudonymAsync(care, assignment.PatientId, ct).ConfigureAwait(false));
        await care.Db.SaveChangesAsync(ct).ConfigureAwait(false);
        return TypedResults.Ok((await HomeworkRowsAsync(care, protector, assignment.PatientId, today, assignment.Id, ct).ConfigureAwait(false)).Single());
    }

    /// <summary>SB-22 cancel (after a confirmation in the portal): the exercise ends today and the patient is told.</summary>
    internal static async Task<NoContent> CancelHomeworkAsync(Guid id, ICurrentUser user, CareRecords care, IAuditLog audit, IClock clock, CancellationToken ct)
    {
        CareRecords.RequireTherapist(user);
        var assignment = await care.Db.HomeworkAssignments.FirstOrDefaultAsync(a => a.Id == id, ct).ConfigureAwait(false)
            ?? throw new DomainException(ErrorCodes.NotFound, $"Homework {id} not found.");
        assignment.Cancel(await care.TodayAsync(assignment.PatientId, clock, ct).ConfigureAwait(false), clock);
        audit.Append("homework.cancelled", "HomeworkAssignment", assignment.Id.ToString(), await PseudonymAsync(care, assignment.PatientId, ct).ConfigureAwait(false));
        await care.Db.SaveChangesAsync(ct).ConfigureAwait(false);
        await care.NotifyAsync(assignment.PatientId, NotificationKinds.HomeworkCancelled, new Dictionary<string, string> { ["homeworkId"] = assignment.Id.ToString() }, $"/homework/{assignment.Id}", ct).ConfigureAwait(false);
        return TypedResults.NoContent();
    }

    /// <summary>SB-23: a reply to one check-in (previewed in the portal); the patient sees it on P10 and P09 and is notified.</summary>
    internal static async Task<NoContent> FeedbackAsync(Guid id, FeedbackRequest request, ICurrentUser user, CareRecords care, IPatientDataProtector protector, IAuditLog audit, IClock clock, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Text) || request.Text.Length > 1000)
        {
            throw MeEndpoints.Invalid("text");
        }

        CareRecords.RequireTherapist(user);
        var checkin = await care.Db.HomeworkCheckins.FirstOrDefaultAsync(c => c.Id == id, ct).ConfigureAwait(false)
            ?? throw new DomainException(ErrorCodes.NotFound, $"Check-in {id} not found.");
        checkin.GiveFeedback(await protector.EncryptAsync(checkin.PatientId, Fields.CheckinFeedback, request.Text.Trim(), ct).ConfigureAwait(false), clock);
        audit.Append("homework.feedback_given", "HomeworkCheckin", checkin.Id.ToString(), await PseudonymAsync(care, checkin.PatientId, ct).ConfigureAwait(false));
        await care.Db.SaveChangesAsync(ct).ConfigureAwait(false);
        await care.NotifyAsync(checkin.PatientId, "homework.feedback", new Dictionary<string, string>
        {
            ["homeworkId"] = checkin.AssignmentId.ToString(),
            ["therapistName"] = await care.StaffNameAsync(user.UserId, await PatientLanguageAsync(care, checkin.PatientId, ct).ConfigureAwait(false), ct).ConfigureAwait(false),
        }, $"/homework/{checkin.AssignmentId}", ct).ConfigureAwait(false);
        return TypedResults.NoContent();
    }

    internal static async Task<List<StaffHomeworkDto>> HomeworkRowsAsync(CareRecords care, IPatientDataProtector protector, Guid patientId, DateOnly today, Guid? only, CancellationToken ct)
    {
        var assignments = await care.Db.HomeworkAssignments.AsNoTracking().Where(a => a.PatientId == patientId && (only == null || a.Id == only)).OrderByDescending(a => a.CreatedAtUtc).ToListAsync(ct).ConfigureAwait(false);
        var ids = assignments.Select(a => a.Id).ToList();
        var checkins = await care.Db.HomeworkCheckins.AsNoTracking().Where(c => ids.Contains(c.AssignmentId)).OrderByDescending(c => c.Day).ToListAsync(ct).ConfigureAwait(false);
        var weekStart = today.AddDays(-(int)today.DayOfWeek);
        var rows = new List<StaffHomeworkDto>();
        foreach (var a in assignments)
        {
            var mine = checkins.Where(c => c.AssignmentId == a.Id).ToList();
            var list = new List<StaffCheckinDto>();
            foreach (var c in mine.Take(14))
            {
                list.Add(new StaffCheckinDto(
                    c.Id,
                    c.Day,
                    c.Status switch { CheckinStatus.Done => "done", CheckinStatus.Partial => "partial", _ => "skipped" },
                    c.Feeling,
                    c.NoteCipher is null ? null : await protector.DecryptAsync(patientId, Fields.CheckinNote, c.NoteCipher, ct).ConfigureAwait(false),
                    c.GateLevel is null ? null : (int)c.GateLevel,
                    c.FeedbackCipher is null ? null : await protector.DecryptAsync(patientId, Fields.CheckinFeedback, c.FeedbackCipher, ct).ConfigureAwait(false),
                    c.FeedbackAtUtc,
                    c.FeedbackSeenAtUtc is not null));
            }

            var feelings = mine.Where(c => c.Feeling is not null).Select(c => c.Feeling!.Value).ToList();
            var status = a.IsCancelled ? "cancelled" : a.IsCompleted(today) ? "ended" : "active";
            rows.Add(new StaffHomeworkDto(
                a.Id,
                await protector.DecryptAsync(patientId, Fields.HomeworkTitle, a.TitleCipher, ct).ConfigureAwait(false),
                JsonSerializer.Deserialize<List<string>>(await protector.DecryptAsync(patientId, Fields.HomeworkInstructions, a.InstructionsCipher, ct).ConfigureAwait(false), PatientContext.Json) ?? [],
                a.Minutes,
                CareEndpoints.FrequencyCode(a.Frequency),
                a.StartsOn,
                a.EndsOn,
                status,
                a.TemplateCode,
                mine.Count(c => c.Day >= weekStart && c.Status != CheckinStatus.Skipped),
                WeeklyTarget(a.Frequency),
                feelings.Count == 0 ? null : Math.Round(feelings.Average(), 1),
                list.FirstOrDefault(),
                list));
        }

        return rows;
    }

    internal static int WeeklyTarget(HomeworkFrequency frequency) => frequency switch
    {
        HomeworkFrequency.Daily => 7,
        HomeworkFrequency.TwiceWeekly => 2,
        _ => 1,
    };

    private static (string Title, List<string> Instructions) CleanHomework(string? title, List<string>? instructions)
    {
        var t = title?.Trim() ?? string.Empty;
        if (t.Length is 0 or > MaxHomeworkTitle)
        {
            throw MeEndpoints.Invalid("title");
        }

        var lines = (instructions ?? []).Select(i => i?.Trim() ?? string.Empty).Where(i => i.Length > 0).ToList();
        if (lines.Count is 0 or > 20 || lines.Any(l => l.Length > 500) || lines.Sum(l => l.Length) > MaxHomeworkInstructions)
        {
            throw MeEndpoints.Invalid("instructions");
        }

        return (t, lines);
    }

    private static HomeworkFrequency ParseFrequency(string? frequency) => frequency switch
    {
        "daily" => HomeworkFrequency.Daily,
        "twice_weekly" => HomeworkFrequency.TwiceWeekly,
        "weekly" => HomeworkFrequency.Weekly,
        "once" => HomeworkFrequency.Once,
        _ => throw MeEndpoints.Invalid("frequency"),
    };

    private static Task<string> PseudonymAsync(CareRecords care, Guid patientId, CancellationToken ct) =>
        care.Db.UserAccounts.AsNoTracking().Where(a => a.Id == patientId).Select(a => a.PseudonymId).FirstAsync(ct);

    private static async Task<string> PatientLanguageAsync(CareRecords care, Guid patientId, CancellationToken ct) =>
        await care.Db.UserAccounts.AsNoTracking().Where(a => a.Id == patientId).Select(a => a.PreferredLanguage).FirstOrDefaultAsync(ct).ConfigureAwait(false) ?? "ar";
}
