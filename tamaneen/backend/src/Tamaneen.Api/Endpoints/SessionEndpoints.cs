using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Tamaneen.Api.Auth;
using Tamaneen.Api.Patients;
using Tamaneen.Api.Sessions;
using Tamaneen.Clinical.Scheduling;
using Tamaneen.Clinical.Sessions;
using Tamaneen.Crisis;
using Tamaneen.Hosting;
using Tamaneen.Infrastructure.Auditing;
using Tamaneen.Infrastructure.Persistence;
using Tamaneen.Infrastructure.Security;
using Tamaneen.SharedKernel;
using Tamaneen.SharedKernel.Notifications;

namespace Tamaneen.Api.Endpoints;

/// <summary>
/// The session itself: lobby (P41, P54), room (P42), after the session (P43) and the therapist's session page.
/// Joining goes through <see cref="IVideoSessionProvider"/> and only inside the join window; presence is a heartbeat
/// that carries the platform's session pass. All queries run as the caller, so row-level security applies.
/// </summary>
public static class SessionEndpoints
{
    public const string PatientSide = "patient";
    public const string TherapistSide = "therapist";

    public sealed record JoinDescriptorDto(string Provider, string Mode, string? Url, string Token, DateTimeOffset ExpiresAt, DateTimeOffset OpensAt, DateTimeOffset ClosesAt, string Participant, int HeartbeatSeconds);

    public sealed record PeerDto(bool Present, DateTimeOffset? JoinedAt, DateTimeOffset? LastSeenAt);

    public sealed record SessionStateDto(Guid AppointmentId, DateTimeOffset ServerNow, DateTimeOffset StartsAt, int DurationMinutes, DateTimeOffset OpensAt, DateTimeOffset ClosesAt,
        bool Open, string Status, PeerDto Peer, bool TherapistLate, int LateOptionsAfterMinutes, int HeartbeatSeconds, string Provider, bool PatientCrisisOpen = false);

    public sealed record PresenceRequest(string? Token, bool? Leaving);

    public sealed record PreSessionNoteDto(string? Text, DateTimeOffset? UpdatedAt);

    public sealed record PreSessionNoteRequest(string? Text);

    public sealed record PreSessionNoteSaved(bool Crisis, int Level, string? Script, string? Text, DateTimeOffset? UpdatedAt);

    public sealed record FeedbackRequest(string? Rating, bool? Comfortable, string? Comment);

    public sealed record SessionFeedbackDto(string? Rating, bool Comfortable, string? Comment, DateTimeOffset CreatedAt);

    public sealed record FeedbackSaved(bool Crisis, int Level, string? Script, SessionFeedbackDto Feedback);

    public sealed record StaffSessionDto(Guid AppointmentId, string PseudonymId, int SequenceNumber, int? RiskLevel, SessionStateDto State, string? PreSessionNote, DateTimeOffset? PreSessionNoteAt);

    public static IEndpointRouteBuilder MapSessionEndpoints(this IEndpointRouteBuilder app)
    {
        var patient = app.MapGroup("/v1/appointments/{id:guid}").RequireAuthorization(AuthSetup.PatientPolicy);
        patient.MapPost("/join", PatientJoinAsync);
        patient.MapGet("/presence", PatientStateAsync);
        patient.MapPost("/presence", PatientPresenceAsync);
        patient.MapGet("/pre-session-note", NoteAsync);
        patient.MapPut("/pre-session-note", SaveNoteAsync);
        patient.MapGet("/feedback", FeedbackAsync);
        patient.MapPost("/feedback", SaveFeedbackAsync).WithIdempotency();

        var staff = app.MapGroup("/v1/staff/appointments/{id:guid}").RequireAuthorization(AuthSetup.ClinicalPolicy);
        staff.MapGet("/session", StaffSessionAsync);
        staff.MapGet("/presence", StaffStateAsync);
        staff.MapPost("/join", StaffJoinAsync);
        staff.MapPost("/presence", StaffPresenceAsync);
        return app;
    }

    // ---------- patient: P41, P42, P54 ----------

    internal static async Task<Ok<JoinDescriptorDto>> PatientJoinAsync(Guid id, PatientContext p, SessionRoom room, CancellationToken ct)
    {
        var a = await PatientAppointmentAsync(p, id, ct).ConfigureAwait(false);
        var pseudonym = (await p.AccountAsync(ct).ConfigureAwait(false)).PseudonymId;
        return TypedResults.Ok(await room.JoinAsync(a, p.PatientId, SessionParticipant.Patient, pseudonym, ct).ConfigureAwait(false));
    }

    internal static async Task<Ok<SessionStateDto>> PatientStateAsync(Guid id, PatientContext p, SessionRoom room, CancellationToken ct)
    {
        var a = await PatientAppointmentAsync(p, id, ct).ConfigureAwait(false);
        return TypedResults.Ok(await room.StateAsync(a, SessionParticipant.Patient, ct).ConfigureAwait(false));
    }

    internal static async Task<Ok<SessionStateDto>> PatientPresenceAsync(Guid id, PresenceRequest request, PatientContext p, SessionRoom room, CancellationToken ct)
    {
        var a = await PatientAppointmentAsync(p, id, ct).ConfigureAwait(false);
        return TypedResults.Ok(await room.BeatAsync(a, p.PatientId, SessionParticipant.Patient, request, ct).ConfigureAwait(false));
    }

    internal static async Task<Ok<PreSessionNoteDto>> NoteAsync(Guid id, PatientContext p, CancellationToken ct)
    {
        await PatientAppointmentAsync(p, id, ct).ConfigureAwait(false);
        var prep = await p.Db.SessionPrep.AsNoTracking().FirstOrDefaultAsync(s => s.AppointmentId == id, ct).ConfigureAwait(false);
        return TypedResults.Ok(prep is null ? new PreSessionNoteDto(null, null) : new PreSessionNoteDto(await p.DecryptAsync(Fields.PreSessionNote, prep.NoteCipher, ct).ConfigureAwait(false), prep.UpdatedAtUtc));
    }

    /// <summary>P41 «عن ماذا تحب أن نتحدث اليوم؟»: up to 1,000 characters, gated, encrypted; an empty text removes the note.</summary>
    internal static async Task<Ok<PreSessionNoteSaved>> SaveNoteAsync(Guid id, PreSessionNoteRequest request, PatientContext p, IAuditLog audit, IClock clock, CancellationToken ct)
    {
        var a = await PatientAppointmentAsync(p, id, ct).ConfigureAwait(false);
        var text = request.Text?.Trim() ?? string.Empty;
        if (text.Length > SessionPrep.MaxLength)
        {
            throw MeEndpoints.Invalid("text");
        }

        if (a.Status != AppointmentStatus.Confirmed || clock.UtcNow > a.StartsAtUtc.AddMinutes(a.DurationMinutes))
        {
            throw new DomainException(ErrorCodes.SessionNoteClosed, $"Appointment {id} is over.");
        }

        var prep = await p.Db.SessionPrep.FirstOrDefaultAsync(s => s.AppointmentId == id, ct).ConfigureAwait(false);
        Gate.GateVerdict? verdict = null;
        if (text.Length == 0)
        {
            if (prep is not null)
            {
                p.Db.SessionPrep.Remove(prep);
            }
        }
        else
        {
            verdict = await p.GateAsync(text, null, "session", ct).ConfigureAwait(false);
            var cipher = await p.EncryptAsync(Fields.PreSessionNote, text, ct).ConfigureAwait(false);
            if (prep is null)
            {
                p.Db.SessionPrep.Add(SessionPrep.Write(a.Id, a.PatientId, a.TherapistUserId, cipher, clock));
            }
            else
            {
                prep.Rewrite(cipher, clock);
            }
        }

        audit.Append("session.prep_saved", "Appointment", a.Id.ToString(), (await p.AccountAsync(ct).ConfigureAwait(false)).PseudonymId);
        await p.Db.SaveChangesAsync(ct).ConfigureAwait(false);
        return TypedResults.Ok(new PreSessionNoteSaved(verdict?.Crisis ?? false, (int)(verdict?.Level ?? RiskLevel.Routine), verdict is { Crisis: true } ? verdict.Script : null,
            text.Length == 0 ? null : text, text.Length == 0 ? null : clock.UtcNow));
    }

    // ---------- patient: P43 ----------

    internal static async Task<Results<Ok<SessionFeedbackDto>, NotFound>> FeedbackAsync(Guid id, PatientContext p, CancellationToken ct)
    {
        await PatientAppointmentAsync(p, id, ct).ConfigureAwait(false);
        var feedback = await p.Db.SessionFeedback.AsNoTracking().FirstOrDefaultAsync(f => f.AppointmentId == id, ct).ConfigureAwait(false);
        return feedback is null ? TypedResults.NotFound() : TypedResults.Ok(await MapAsync(p, feedback, ct).ConfigureAwait(false));
    }

    /// <summary>
    /// P43: one private rating per session (409 once rated), for supervisors only. The optional comment passes the crisis
    /// gate and is stored encrypted with the patient's key. Idempotency-Key makes a resend answer with the first result.
    /// </summary>
    internal static async Task<Ok<FeedbackSaved>> SaveFeedbackAsync(Guid id, FeedbackRequest request, PatientContext p, IAuditLog audit, IClock clock, CancellationToken ct)
    {
        var a = await PatientAppointmentAsync(p, id, ct).ConfigureAwait(false);
        var comment = request.Comment?.Trim();
        if (comment is { Length: > SessionFeedback.MaxCommentLength })
        {
            throw MeEndpoints.Invalid("comment");
        }

        byte? rating;
        try
        {
            rating = SessionFeedback.ParseRating(request.Rating);
        }
        catch (DomainException)
        {
            throw MeEndpoints.Invalid("rating");
        }

        if (rating is null && request.Comfortable != true && string.IsNullOrEmpty(comment))
        {
            throw MeEndpoints.Invalid("rating");
        }

        if (!SessionWindow.FeedbackAllowed(a, clock.UtcNow))
        {
            throw new DomainException(ErrorCodes.SessionFeedbackNotYet, $"Appointment {id} cannot be rated now.");
        }

        if (await p.Db.SessionFeedback.AnyAsync(f => f.AppointmentId == id, ct).ConfigureAwait(false))
        {
            throw new DomainException(ErrorCodes.SessionFeedbackExists, $"Appointment {id} already rated.");
        }

        Gate.GateVerdict? verdict = null;
        if (!string.IsNullOrEmpty(comment))
        {
            verdict = await p.GateAsync(comment, null, "session", ct).ConfigureAwait(false);
        }

        var feedback = SessionFeedback.Record(a.Id, a.PatientId, a.TherapistUserId, rating, request.Comfortable == true,
            await p.EncryptOptionalAsync(Fields.SessionFeedbackComment, comment, ct).ConfigureAwait(false), clock);
        p.Db.SessionFeedback.Add(feedback);
        audit.Append("session.feedback_given", "Appointment", a.Id.ToString(), (await p.AccountAsync(ct).ConfigureAwait(false)).PseudonymId);
        try
        {
            await p.Db.SaveChangesAsync(ct).ConfigureAwait(false);
        }
        catch (DbUpdateException ex) when (ex.InnerException?.Message.Contains("UX_SessionFeedback_Appointment", StringComparison.Ordinal) == true)
        {
            throw new DomainException(ErrorCodes.SessionFeedbackExists, $"Appointment {id} already rated.");
        }

        return TypedResults.Ok(new FeedbackSaved(verdict?.Crisis ?? false, (int)(verdict?.Level ?? RiskLevel.Routine), verdict is { Crisis: true } ? verdict.Script : null,
            await MapAsync(p, feedback, ct).ConfigureAwait(false)));
    }

    private static async Task<SessionFeedbackDto> MapAsync(PatientContext p, SessionFeedback f, CancellationToken ct) =>
        new(f.RatingCode, f.Comfortable, await p.DecryptOptionalAsync(Fields.SessionFeedbackComment, f.CommentCipher, ct).ConfigureAwait(false), f.CreatedAtUtc);

    // ---------- therapist session page ----------

    /// <summary>The therapist's session page: the appointment, the patient's presence and pre-session note (audited `session.opened`).</summary>
    internal static async Task<Ok<StaffSessionDto>> StaffSessionAsync(Guid id, ICurrentUser user, TamaneenDbContext db, IPatientDataProtector protector, SessionRoom room, IAuditLog audit, CancellationToken ct)
    {
        var a = await StaffAppointmentAsync(db, user, id, ct).ConfigureAwait(false);
        var pseudonym = await db.UserAccounts.AsNoTracking().Where(u => u.Id == a.PatientId).Select(u => u.PseudonymId).FirstAsync(ct).ConfigureAwait(false);
        var triageLevel = await db.TriageCases.AsNoTracking().Where(t => t.PatientId == a.PatientId).OrderByDescending(t => t.SubmittedAtUtc).Select(t => (int?)t.RiskLevel).FirstOrDefaultAsync(ct).ConfigureAwait(false);
        var crisisLevel = await db.CrisisEvents.AsNoTracking().Where(e => e.PatientId == a.PatientId).Select(e => (int?)e.Level).MaxAsync(ct).ConfigureAwait(false);
        int? risk = triageLevel is null && crisisLevel is null ? null : Math.Max(triageLevel ?? 0, crisisLevel ?? 0);
        var prep = await db.SessionPrep.AsNoTracking().FirstOrDefaultAsync(s => s.AppointmentId == id, ct).ConfigureAwait(false);
        var note = prep is null ? null : await protector.DecryptAsync(a.PatientId, Fields.PreSessionNote, prep.NoteCipher, ct).ConfigureAwait(false);
        audit.Append("session.opened", "Appointment", a.Id.ToString(), pseudonym, prep is null ? null : "pre_session_note");
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        var state = await room.StateAsync(a, SessionParticipant.Therapist, ct).ConfigureAwait(false);
        return TypedResults.Ok(new StaffSessionDto(a.Id, pseudonym, a.SequenceNumber, risk, state, note, prep?.UpdatedAtUtc));
    }

    internal static async Task<Ok<SessionStateDto>> StaffStateAsync(Guid id, ICurrentUser user, TamaneenDbContext db, SessionRoom room, CancellationToken ct)
    {
        var a = await StaffAppointmentAsync(db, user, id, ct).ConfigureAwait(false);
        return TypedResults.Ok(await room.StateAsync(a, SessionParticipant.Therapist, ct).ConfigureAwait(false));
    }

    internal static async Task<Ok<JoinDescriptorDto>> StaffJoinAsync(Guid id, ICurrentUser user, TamaneenDbContext db, SessionRoom room, CancellationToken ct)
    {
        var a = await StaffAppointmentAsync(db, user, id, ct).ConfigureAwait(false);
        var pseudonym = await db.UserAccounts.AsNoTracking().Where(u => u.Id == a.PatientId).Select(u => u.PseudonymId).FirstAsync(ct).ConfigureAwait(false);
        return TypedResults.Ok(await room.JoinAsync(a, user.UserId!.Value, SessionParticipant.Therapist, pseudonym, ct).ConfigureAwait(false));
    }

    internal static async Task<Ok<SessionStateDto>> StaffPresenceAsync(Guid id, PresenceRequest request, ICurrentUser user, TamaneenDbContext db, SessionRoom room, CancellationToken ct)
    {
        var a = await StaffAppointmentAsync(db, user, id, ct).ConfigureAwait(false);
        return TypedResults.Ok(await room.BeatAsync(a, user.UserId!.Value, SessionParticipant.Therapist, request, ct).ConfigureAwait(false));
    }

    // ---------- helpers ----------

    private static async Task<Appointment> PatientAppointmentAsync(PatientContext p, Guid id, CancellationToken ct) =>
        await p.Db.Appointments.FirstOrDefaultAsync(a => a.Id == id && a.PatientId == p.PatientId, ct).ConfigureAwait(false)
            ?? throw new DomainException(ErrorCodes.NotFound, $"Appointment {id} not found.");

    /// <summary>Only the session's own therapist: anyone else (another therapist, a supervisor) gets 404.</summary>
    private static async Task<Appointment> StaffAppointmentAsync(TamaneenDbContext db, ICurrentUser user, Guid id, CancellationToken ct) =>
        await db.Appointments.FirstOrDefaultAsync(a => a.Id == id && a.TherapistUserId == user.UserId, ct).ConfigureAwait(false)
            ?? throw new DomainException(ErrorCodes.NotFound, $"Appointment {id} not found.");
}

/// <summary>Join window, provider, session pass, presence and the notifications around them, for both sides.</summary>
public sealed class SessionRoom(TamaneenDbContext db, IVideoSessionProvider provider, SessionPassSigner passes, IOptions<VideoOptions> options, INotifier notifier, IAuditLog audit, IClock clock)
{
    private VideoOptions Options => options.Value;

    public async Task<SessionEndpoints.JoinDescriptorDto> JoinAsync(Appointment a, Guid userId, SessionParticipant side, string pseudonym, CancellationToken ct)
    {
        var now = clock.UtcNow;
        var (patient, therapist) = await PresenceAsync(a.Id, ct).ConfigureAwait(false);
        var therapistJoined = therapist?.FirstJoinedAtUtc;
        if (!SessionWindow.IsOpen(a, now, therapistJoined))
        {
            throw new DomainException(ErrorCodes.SessionNotOpen, $"Appointment {a.Id} room is not open.")
                .With("opensAt", SessionWindow.OpensAt(a)).With("closesAt", SessionWindow.ClosesAt(a, therapistJoined));
        }

        var mine = side == SessionParticipant.Patient ? patient : therapist;
        var firstTherapistJoin = side == SessionParticipant.Therapist && therapist is null;
        var closes = SessionWindow.ClosesAt(a, therapistJoined ?? (firstTherapistJoin ? now : null));
        var expires = now.AddMinutes(Options.TokenMinutes);
        if (expires > closes)
        {
            expires = closes;
        }

        var role = Side(side);
        var room = await provider.EnsureRoomAsync(new VideoRoomRequest(a.Id, a.RoomId, SessionWindow.OpensAt(a), closes), ct).ConfigureAwait(false);
        var join = await provider.IssueJoinAsync(room, new VideoParticipant(userId, role), expires, ct).ConfigureAwait(false);
        var pass = passes.Issue(new SessionPassClaims(a.Id, userId, role, expires));

        if (mine is null)
        {
            db.SessionPresence.Add(SessionPresence.Join(a.Id, a.PatientId, a.TherapistUserId, side, clock));
        }
        else
        {
            mine.Beat(clock);
        }

        audit.Append("session.joined", "Appointment", a.Id.ToString(), pseudonym, $"{role}:{provider.Name}");
        if (firstTherapistJoin && now > a.StartsAtUtc)
        {
            // P54: lateness is recorded for the supervisor's quality review.
            audit.Append("session.therapist_late", "Appointment", a.Id.ToString(), pseudonym, $"minutes={(int)(now - a.StartsAtUtc).TotalMinutes}");
        }

        try
        {
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
        }
        catch (DbUpdateException ex) when (ex.InnerException?.Message.Contains("UX_SessionPresence_AppointmentParticipant", StringComparison.Ordinal) == true)
        {
            // A second tab joined at the same moment: the other request recorded the presence and its audit row.
            firstTherapistJoin = false;
        }

        if (firstTherapistJoin)
        {
            var name = await db.StaffProfiles.AsNoTracking().FirstOrDefaultAsync(s => s.UserId == a.TherapistUserId, ct).ConfigureAwait(false);
            var language = await db.UserAccounts.AsNoTracking().Where(u => u.Id == a.PatientId).Select(u => u.PreferredLanguage).FirstOrDefaultAsync(ct).ConfigureAwait(false) ?? "ar";
            await notifier.NotifyAsync(new Notification(a.PatientId, "session.therapist_joined", new Dictionary<string, string>
            {
                ["appointmentId"] = a.Id.ToString(),
                ["startsAt"] = a.StartsAtUtc.ToString("O", System.Globalization.CultureInfo.InvariantCulture),
                ["therapistName"] = name?.DisplayName(language) ?? string.Empty,
            }, $"/appointments/{a.Id}/lobby"), ct).ConfigureAwait(false);
        }

        return new SessionEndpoints.JoinDescriptorDto(provider.Name, join.Mode, join.Url, pass, expires, SessionWindow.OpensAt(a), closes, role, Options.HeartbeatSeconds);
    }

    public async Task<SessionEndpoints.SessionStateDto> BeatAsync(Appointment a, Guid userId, SessionParticipant side, SessionEndpoints.PresenceRequest request, CancellationToken ct)
    {
        passes.Validate(request.Token, a.Id, userId, Side(side), clock.UtcNow);
        var (patient, therapist) = await PresenceAsync(a.Id, ct).ConfigureAwait(false);
        var mine = side == SessionParticipant.Patient ? patient : therapist;
        if (mine is null)
        {
            // A pass is only issued by a join, which records the presence; this is a row lost to a race. Record it again.
            mine = SessionPresence.Join(a.Id, a.PatientId, a.TherapistUserId, side, clock);
            db.SessionPresence.Add(mine);
        }

        if (request.Leaving == true)
        {
            mine.Leave(clock);
        }
        else
        {
            mine.Beat(clock);
        }

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return await StateAsync(a, side, ct).ConfigureAwait(false);
    }

    public async Task<SessionEndpoints.SessionStateDto> StateAsync(Appointment a, SessionParticipant side, CancellationToken ct)
    {
        var now = clock.UtcNow;
        var (patient, therapist) = await PresenceAsync(a.Id, ct).ConfigureAwait(false);
        var peer = side == SessionParticipant.Patient ? therapist : patient;
        var therapistJoined = therapist?.FirstJoinedAtUtc;
        var crisisOpen = side == SessionParticipant.Therapist
            && await db.CrisisEvents.AsNoTracking().AnyAsync(e => e.PatientId == a.PatientId && e.Status != CrisisEventStatus.Closed, ct).ConfigureAwait(false);
        return new SessionEndpoints.SessionStateDto(
            a.Id, now, a.StartsAtUtc, a.DurationMinutes, SessionWindow.OpensAt(a), SessionWindow.ClosesAt(a, therapistJoined),
            SessionWindow.IsOpen(a, now, therapistJoined), SchedulingEndpoints.StatusCode(a.Status),
            new SessionEndpoints.PeerDto(peer?.IsPresent(now) ?? false, peer?.FirstJoinedAtUtc, peer?.LastSeenAtUtc),
            SessionWindow.TherapistLate(a, now, therapistJoined), Options.LateOptionsAfterMinutes, Options.HeartbeatSeconds, provider.Name, crisisOpen);
    }

    /// <summary>P54: whether the therapist has joined yet (the reschedule rule needs it).</summary>
    public async Task<DateTimeOffset?> TherapistJoinedAtAsync(Guid appointmentId, CancellationToken ct) =>
        (await PresenceAsync(appointmentId, ct).ConfigureAwait(false)).Therapist?.FirstJoinedAtUtc;

    private async Task<(SessionPresence? Patient, SessionPresence? Therapist)> PresenceAsync(Guid appointmentId, CancellationToken ct)
    {
        var rows = await db.SessionPresence.Where(s => s.AppointmentId == appointmentId).ToListAsync(ct).ConfigureAwait(false);
        return (rows.FirstOrDefault(r => r.Participant == SessionParticipant.Patient), rows.FirstOrDefault(r => r.Participant == SessionParticipant.Therapist));
    }

    private static string Side(SessionParticipant side) => side == SessionParticipant.Patient ? SessionEndpoints.PatientSide : SessionEndpoints.TherapistSide;
}
