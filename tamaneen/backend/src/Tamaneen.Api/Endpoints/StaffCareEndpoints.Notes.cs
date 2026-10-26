using System.Globalization;
using System.Text.Json;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Tamaneen.Api.Clinical;
using Tamaneen.Api.Patients;
using Tamaneen.Clinical.Care;
using Tamaneen.Clinical.Scheduling;
using Tamaneen.Crisis;
using Tamaneen.Infrastructure.Auditing;
using Tamaneen.Infrastructure.Security;
using Tamaneen.SharedKernel;

namespace Tamaneen.Api.Endpoints;

/// <summary>
/// S33 session notes, one per appointment. The therapist saves drafts (If-Match with the note's revision, "0"
/// before the first save), then signs; a signed note is read-only and only takes addenda. Notes are encrypted with
/// the patient's key and row-level security never shows them to the patient. Every read and write is audited.
/// On signing, up to three plain-language points can be shared with the patient (SessionSummaries).
/// </summary>
public static partial class StaffCareEndpoints
{
    public const int MaxSummaryPoints = 3;
    public const int MaxSummaryPoint = 300;

    public sealed record NoteAppointmentDto(Guid Id, DateTimeOffset StartsAt, int DurationMinutes, int SequenceNumber, string Status);

    public sealed record NoteAddendumDto(Guid Id, string Text, string AuthorName, DateTimeOffset At);

    public sealed record SessionNoteDto(Guid Id, string Template, IReadOnlyDictionary<string, string> Fields, IReadOnlyList<string> SummaryPoints, bool ShareSummary, string Status, int Revision, DateTimeOffset UpdatedAt, DateTimeOffset? SignedAt, string? SignedBy, IReadOnlyList<NoteAddendumDto> Addenda);

    public sealed record LastWeekDto(int HomeworkDone, int HomeworkTarget, double? MoodAverage, int Alerts);

    public sealed record SessionNoteViewDto(string PseudonymId, NoteAppointmentDto Appointment, SessionNoteDto? Note, LastWeekDto LastWeek, bool CanEdit, IReadOnlyDictionary<string, string[]> Templates);

    public sealed record SaveNoteRequest(string? Template, Dictionary<string, string>? Fields, List<string>? SummaryPoints, bool ShareSummary);

    public sealed record AddendumRequest(string? Text);

    internal static async Task<Ok<SessionNoteViewDto>> NoteAsync(string pseudonymId, Guid appointmentId, HttpContext http, ICurrentUser user, CareRecords care, IPatientDataProtector protector, IAuditLog audit, IClock clock, CancellationToken ct)
    {
        var (patientId, appointment) = await NoteAppointmentAsync(care, pseudonymId, appointmentId, ct).ConfigureAwait(false);
        var note = await care.Db.SessionNotes.AsNoTracking().FirstOrDefaultAsync(n => n.AppointmentId == appointmentId, ct).ConfigureAwait(false);
        audit.Append("patient.note_opened", "Appointment", appointmentId.ToString(), StaffEndpoints.Canonical(pseudonymId));
        await care.Db.SaveChangesAsync(ct).ConfigureAwait(false);
        return TypedResults.Ok(await NoteViewAsync(http, pseudonymId, patientId, appointment, note, user, care, protector, clock, ct).ConfigureAwait(false));
    }

    /// <summary>Creates the draft or saves it again (autosave every 20–30 s from the portal). 412 when another save came first; 409 once signed.</summary>
    internal static async Task<Ok<SessionNoteViewDto>> SaveNoteAsync(string pseudonymId, Guid appointmentId, SaveNoteRequest request, HttpContext http, ICurrentUser user, CareRecords care, IPatientDataProtector protector, IAuditLog audit, IClock clock, CancellationToken ct)
    {
        CareRecords.RequireTherapist(user);
        var (patientId, appointment) = await NoteAppointmentAsync(care, pseudonymId, appointmentId, ct).ConfigureAwait(false);
        EnsureNoteWriter(appointment, user, clock);
        var template = request.Template ?? string.Empty;
        if (!SessionNote.Templates.TryGetValue(template, out var keys))
        {
            throw MeEndpoints.Invalid("template");
        }

        var fields = keys.ToDictionary(k => k, k => (request.Fields ?? []).GetValueOrDefault(k)?.Trim() ?? string.Empty, StringComparer.Ordinal);
        if (fields.Values.Any(v => v.Length > SessionNote.MaxFieldLength))
        {
            throw MeEndpoints.Invalid("fields");
        }

        var points = (request.SummaryPoints ?? []).Select(p => p?.Trim() ?? string.Empty).Where(p => p.Length > 0).ToList();
        if (points.Count > MaxSummaryPoints || points.Any(p => p.Length > MaxSummaryPoint))
        {
            throw MeEndpoints.Invalid("summaryPoints");
        }

        var note = await care.Db.SessionNotes.FirstOrDefaultAsync(n => n.AppointmentId == appointmentId, ct).ConfigureAwait(false);
        CareRecords.RequireVersion(http, (note?.Revision ?? 0).ToString(CultureInfo.InvariantCulture));
        if (note is null)
        {
            note = SessionNote.Start(patientId, user.UserId!.Value, appointmentId, clock);
            care.Db.SessionNotes.Add(note);
        }

        note.SaveDraft(
            template,
            await protector.EncryptAsync(patientId, Fields.SessionNote, CareRecords.Json(fields), ct).ConfigureAwait(false),
            points.Count == 0 ? null : await protector.EncryptAsync(patientId, Fields.SessionNoteSummary, CareRecords.Json(points), ct).ConfigureAwait(false),
            request.ShareSummary,
            clock);
        audit.Append("patient.note_saved", "SessionNote", note.Id.ToString(), StaffEndpoints.Canonical(pseudonymId));
        await SaveNoteChangesAsync(care, ct).ConfigureAwait(false);
        return TypedResults.Ok(await NoteViewAsync(http, pseudonymId, patientId, appointment, note, user, care, protector, clock, ct).ConfigureAwait(false));
    }

    /// <summary>Signs and locks the note (after a confirmation). With "share", the points go to the patient.</summary>
    internal static async Task<Ok<SessionNoteViewDto>> SignNoteAsync(string pseudonymId, Guid appointmentId, HttpContext http, ICurrentUser user, CareRecords care, IPatientDataProtector protector, IAuditLog audit, IClock clock, CancellationToken ct)
    {
        CareRecords.RequireTherapist(user);
        var (patientId, appointment) = await NoteAppointmentAsync(care, pseudonymId, appointmentId, ct).ConfigureAwait(false);
        EnsureNoteWriter(appointment, user, clock);
        var note = await care.Db.SessionNotes.FirstOrDefaultAsync(n => n.AppointmentId == appointmentId, ct).ConfigureAwait(false)
            ?? throw new DomainException(ErrorCodes.NotFound, "No draft to sign.");
        CareRecords.RequireVersion(http, note.Revision.ToString(CultureInfo.InvariantCulture));
        note.Sign(user.UserId!.Value, clock);
        var share = note.ShareSummary && note.SummaryCipher is not null;
        if (share)
        {
            var points = await protector.DecryptAsync(patientId, Fields.SessionNoteSummary, note.SummaryCipher!, ct).ConfigureAwait(false);
            care.Db.SessionSummaries.Add(SessionSummary.Share(note, await protector.EncryptAsync(patientId, Fields.SessionSummary, points, ct).ConfigureAwait(false), clock));
        }

        audit.Append("patient.note_signed", "SessionNote", note.Id.ToString(), StaffEndpoints.Canonical(pseudonymId), share ? "summary_shared" : null);
        await SaveNoteChangesAsync(care, ct).ConfigureAwait(false);
        if (share)
        {
            await care.NotifyAsync(patientId, "session.summary_shared", new Dictionary<string, string> { ["appointmentId"] = appointmentId.ToString() }, $"/appointments/{appointmentId}/summary", ct).ConfigureAwait(false);
        }

        return TypedResults.Ok(await NoteViewAsync(http, pseudonymId, patientId, appointment, note, user, care, protector, clock, ct).ConfigureAwait(false));
    }

    internal static async Task<Ok<SessionNoteViewDto>> AddAddendumAsync(string pseudonymId, Guid appointmentId, AddendumRequest request, HttpContext http, ICurrentUser user, CareRecords care, IPatientDataProtector protector, IAuditLog audit, IClock clock, CancellationToken ct)
    {
        CareRecords.RequireTherapist(user);
        var text = request.Text?.Trim() ?? string.Empty;
        if (text.Length is 0 or > SessionNote.MaxFieldLength)
        {
            throw MeEndpoints.Invalid("text");
        }

        var (patientId, appointment) = await NoteAppointmentAsync(care, pseudonymId, appointmentId, ct).ConfigureAwait(false);
        var note = await care.Db.SessionNotes.FirstOrDefaultAsync(n => n.AppointmentId == appointmentId, ct).ConfigureAwait(false)
            ?? throw new DomainException(ErrorCodes.NotFound, "No note for this appointment.");
        var addendum = SessionNoteAddendum.Add(note, user.UserId!.Value, await protector.EncryptAsync(patientId, Fields.SessionNoteAddendum, text, ct).ConfigureAwait(false), clock);
        care.Db.SessionNoteAddenda.Add(addendum);
        note.Touch(clock);
        audit.Append("patient.note_addendum", "SessionNote", note.Id.ToString(), StaffEndpoints.Canonical(pseudonymId));
        await SaveNoteChangesAsync(care, ct).ConfigureAwait(false);
        return TypedResults.Ok(await NoteViewAsync(http, pseudonymId, patientId, appointment, note, user, care, protector, clock, ct).ConfigureAwait(false));
    }

    /// <summary>Patient side: the points the therapist shared after the session (P43), or 404.</summary>
    internal static async Task<Ok<SessionSummaryDto>> SessionSummaryAsync(Guid id, PatientContext p, CancellationToken ct)
    {
        var row = await p.Db.SessionSummaries.AsNoTracking().FirstOrDefaultAsync(s => s.AppointmentId == id && s.PatientId == p.PatientId, ct).ConfigureAwait(false)
            ?? throw new DomainException(ErrorCodes.NotFound, "No summary shared for this session.");
        var points = JsonSerializer.Deserialize<List<string>>(await p.DecryptAsync(Fields.SessionSummary, row.PointsCipher, ct).ConfigureAwait(false), PatientContext.Json) ?? [];
        return TypedResults.Ok(new SessionSummaryDto(points, row.SharedAtUtc));
    }

    public sealed record SessionSummaryDto(IReadOnlyList<string> Points, DateTimeOffset SharedAt);

    private static async Task<(Guid PatientId, Appointment Appointment)> NoteAppointmentAsync(CareRecords care, string pseudonymId, Guid appointmentId, CancellationToken ct)
    {
        var patientId = await StaffEndpoints.PatientIdAsync(care.Db, pseudonymId, ct).ConfigureAwait(false);
        var appointment = await care.Db.Appointments.AsNoTracking().FirstOrDefaultAsync(a => a.Id == appointmentId && a.PatientId == patientId, ct).ConfigureAwait(false)
            ?? throw new DomainException(ErrorCodes.NotFound, $"Appointment {appointmentId} not found.");
        return (patientId, appointment);
    }

    /// <summary>Only the session's own therapist writes its note, from the start of the session on.</summary>
    private static void EnsureNoteWriter(Appointment appointment, ICurrentUser user, IClock clock)
    {
        if (appointment.TherapistUserId != user.UserId)
        {
            throw new DomainException(ErrorCodes.NotFound, "Not this therapist's session.");
        }

        if (appointment.StartsAtUtc > clock.UtcNow || appointment.Status is AppointmentStatus.CancelledByPatient or AppointmentStatus.CancelledByTherapist)
        {
            throw new DomainException(ErrorCodes.AppointmentNotStarted, $"Appointment {appointment.Id} has not taken place.");
        }
    }

    private static async Task SaveNoteChangesAsync(CareRecords care, CancellationToken ct)
    {
        try
        {
            await care.Db.SaveChangesAsync(ct).ConfigureAwait(false);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new DomainException(ErrorCodes.VersionChanged, "The note changed concurrently.");
        }
        catch (DbUpdateException ex) when (ex.InnerException?.Message.Contains("IX_SessionNotes_AppointmentId", StringComparison.Ordinal) == true)
        {
            throw new DomainException(ErrorCodes.VersionChanged, "The note was created concurrently.");
        }
    }

    private static async Task<SessionNoteViewDto> NoteViewAsync(HttpContext http, string pseudonymId, Guid patientId, Appointment a, SessionNote? note, ICurrentUser user, CareRecords care, IPatientDataProtector protector, IClock clock, CancellationToken ct)
    {
        SessionNoteDto? dto = null;
        if (note is not null)
        {
            var fields = JsonSerializer.Deserialize<Dictionary<string, string>>(await protector.DecryptAsync(patientId, Fields.SessionNote, note.FieldsCipher, ct).ConfigureAwait(false), PatientContext.Json) ?? [];
            var points = note.SummaryCipher is null ? [] : JsonSerializer.Deserialize<List<string>>(await protector.DecryptAsync(patientId, Fields.SessionNoteSummary, note.SummaryCipher, ct).ConfigureAwait(false), PatientContext.Json) ?? [];
            var addenda = await care.Db.SessionNoteAddenda.AsNoTracking().Where(x => x.NoteId == note.Id).OrderBy(x => x.CreatedAtUtc).ToListAsync(ct).ConfigureAwait(false);
            var names = await NamesAsync(care, addenda.Select(x => x.AuthorUserId).Append(note.SignedByUserId ?? note.TherapistUserId), user.Language, ct).ConfigureAwait(false);
            var list = new List<NoteAddendumDto>();
            foreach (var x in addenda)
            {
                list.Add(new NoteAddendumDto(x.Id, await protector.DecryptAsync(patientId, Fields.SessionNoteAddendum, x.TextCipher, ct).ConfigureAwait(false), names.GetValueOrDefault(x.AuthorUserId, string.Empty), x.CreatedAtUtc));
            }

            dto = new SessionNoteDto(note.Id, note.Template, fields, points, note.ShareSummary, note.Status == SessionNoteStatus.Locked ? "signed" : "draft", note.Revision, note.UpdatedAtUtc, note.SignedAtUtc,
                note.SignedByUserId is { } by ? names.GetValueOrDefault(by) : null, list);
        }

        CareRecords.SendVersion(http, (note?.Revision ?? 0).ToString(CultureInfo.InvariantCulture));
        return new SessionNoteViewDto(
            StaffEndpoints.Canonical(pseudonymId),
            new NoteAppointmentDto(a.Id, a.StartsAtUtc, a.DurationMinutes, a.SequenceNumber, SchedulingEndpoints.StatusCode(a.Status)),
            dto,
            await LastWeekAsync(care, patientId, clock, ct).ConfigureAwait(false),
            user.Role == AppRoles.Therapist && a.TherapistUserId == user.UserId,
            SessionNote.Templates);
    }

    /// <summary>"Last week" panel: homework done against the target, the mood average and crisis alerts of the last 7 days.</summary>
    private static async Task<LastWeekDto> LastWeekAsync(CareRecords care, Guid patientId, IClock clock, CancellationToken ct)
    {
        var since = clock.UtcNow.AddDays(-7);
        var sinceDay = DateOnly.FromDateTime(since.UtcDateTime);
        var assignments = await care.Db.HomeworkAssignments.AsNoTracking().Where(h => h.PatientId == patientId && h.EndsOn >= sinceDay && h.CancelledAtUtc == null).ToListAsync(ct).ConfigureAwait(false);
        var ids = assignments.Select(h => h.Id).ToList();
        var done = await care.Db.HomeworkCheckins.AsNoTracking().CountAsync(c => ids.Contains(c.AssignmentId) && c.Day >= sinceDay && c.Status != CheckinStatus.Skipped, ct).ConfigureAwait(false);
        var moods = await care.Db.MoodEntries.AsNoTracking().Where(m => m.PatientId == patientId && m.RecordedAtUtc >= since).Select(m => m.Score).ToListAsync(ct).ConfigureAwait(false);
        var alerts = await care.Db.CrisisEvents.AsNoTracking().CountAsync(e => e.PatientId == patientId && e.OpenedAtUtc >= since, ct).ConfigureAwait(false);
        return new LastWeekDto(done, assignments.Sum(h => WeeklyTarget(h.Frequency)), moods.Count == 0 ? null : Math.Round(moods.Average(), 1), alerts);
    }
}
