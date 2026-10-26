using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Tamaneen.Api.Clinical;
using Tamaneen.Clinical.Scheduling;
using Tamaneen.Hosting;
using Tamaneen.Infrastructure.Auditing;
using Tamaneen.Infrastructure.Persistence;
using Tamaneen.SharedKernel;

namespace Tamaneen.Api.Endpoints;

/// <summary>
/// S19 availability (SVC-49, SB-28): the usual week, the session length and gap, leave and absences, and one-off
/// times. Saving generates the bookable times of the next eight weeks. A therapist changes only their own times;
/// supervisors and the clinical lead may read any therapist's. Booked and held times are never removed or moved.
/// Every time that opens runs the waitlist check (P56), the internal <c>availability.slot_opened</c> event.
/// </summary>
public static class AvailabilityEndpoints
{
    public sealed record PeriodDto(int Day, string Start, string End);

    public sealed record BreakDto(Guid? Id, string Kind, DateTimeOffset StartsAt, DateTimeOffset EndsAt);

    public sealed record AvailabilitySlotDto(Guid SlotId, DateTimeOffset StartsAt, DateTimeOffset EndsAt, string Status, string Source);

    public sealed record AvailabilityDto(
        Guid TherapistId,
        string TimeZone,
        int SessionMinutes,
        IReadOnlyList<int> SessionLengths,
        int BufferMinutes,
        IReadOnlyList<int> BufferOptions,
        IReadOnlyList<PeriodDto> Weekly,
        IReadOnlyList<BreakDto> Exceptions,
        int HorizonDays,
        bool LicenseVerified,
        DateTimeOffset? UpdatedAt,
        IReadOnlyList<AvailabilitySlotDto> Slots,
        IReadOnlyList<AvailabilitySlotDto> Affected);

    public sealed record SaveAvailabilityRequest(int? SessionMinutes, int? BufferMinutes, List<PeriodDto>? Weekly, List<BreakDto>? Exceptions);

    public sealed record SaveResultDto(int Added, int Removed, IReadOnlyList<AvailabilitySlotDto> Affected, AvailabilityDto Availability);

    public sealed record PreviewDto(int Added, int Removed, IReadOnlyList<AvailabilitySlotDto> Affected);

    public sealed record SlotRequest(DateTimeOffset? StartsAt);

    public static IEndpointRouteBuilder MapAvailabilityEndpoints(this IEndpointRouteBuilder app)
    {
        var read = app.MapGroup("/v1/staff/availability").RequireAuthorization(AuthSetup.ClinicalPolicy);
        read.MapGet("", GetAsync);

        var write = app.MapGroup("/v1/staff/availability").RequireAuthorization(p => p.RequireAuthenticatedUser().RequireRole(AppRoles.Therapist));
        write.MapPut("", SaveAsync);
        write.MapPost("/preview", PreviewAsync);
        write.MapPost("/slots", AddSlotAsync);
        write.MapPatch("/slots/{slotId:guid}", MoveSlotAsync);
        write.MapDelete("/slots/{slotId:guid}", RemoveSlotAsync);
        return app;
    }

    internal static async Task<Ok<AvailabilityDto>> GetAsync(HttpContext http, ICurrentUser user, TamaneenDbContext db, IOptions<PlatformOptions> platform, IClock clock, CancellationToken ct)
    {
        var therapistId = user.UserId!.Value;
        if (Guid.TryParse(http.Request.Query["therapistId"], out var asked) && asked != therapistId)
        {
            // Supervisors and the clinical lead read any therapist's week (S19 is the therapist's own page).
            if (user.Role == AppRoles.Therapist)
            {
                throw new DomainException(ErrorCodes.NotFound, "Not your availability.");
            }

            therapistId = asked;
        }

        var profile = await db.TherapistProfiles.AsNoTracking().FirstOrDefaultAsync(t => t.UserId == therapistId, ct).ConfigureAwait(false)
            ?? throw new DomainException(ErrorCodes.NotFound, "Not a therapist.");
        var availability = await db.TherapistAvailabilities.AsNoTracking().FirstOrDefaultAsync(a => a.TherapistUserId == therapistId, ct).ConfigureAwait(false)
            ?? TherapistAvailability.Empty(therapistId);
        return TypedResults.Ok(await BuildAsync(db, profile, availability, platform.Value, clock, ct).ConfigureAwait(false));
    }

    internal static async Task<Ok<PreviewDto>> PreviewAsync(SaveAvailabilityRequest request, ICurrentUser user, TamaneenDbContext db, IOptions<PlatformOptions> platform, IClock clock, CancellationToken ct)
    {
        var (profile, availability) = await LoadForWriteAsync(db, user, ct).ConfigureAwait(false);
        var (weekly, breaks, session, buffer) = Parse(request, profile, availability);
        var errors = TherapistAvailability.Validate(weekly, breaks, buffer);
        if (errors.Count > 0)
        {
            throw new DomainException(ErrorCodes.Validation, "Invalid availability.").With("errors", errors);
        }

        var (plan, _) = await PlanAsync(db, profile, weekly, breaks, session, buffer, platform.Value, clock, ct).ConfigureAwait(false);
        return TypedResults.Ok(new PreviewDto(profile.LicenseVerified ? plan.ToAdd.Count : 0, plan.ToRemove.Count, plan.Affected.Select(Dto).ToList()));
    }

    internal static async Task<Ok<SaveResultDto>> SaveAsync(SaveAvailabilityRequest request, ICurrentUser user, TamaneenDbContext db, WaitlistNotifier waitlist, IAuditLog audit, IOptions<PlatformOptions> platform, IClock clock, CancellationToken ct)
    {
        var (profile, availability) = await LoadForWriteAsync(db, user, ct).ConfigureAwait(false);
        var (weekly, breaks, session, buffer) = Parse(request, profile, availability);
        availability.Set(weekly, breaks, buffer, clock);
        if (session != profile.SessionMinutes)
        {
            profile.SetSessionMinutes(session);
        }

        var (plan, referenced) = await PlanAsync(db, profile, weekly, breaks, session, buffer, platform.Value, clock, ct).ConfigureAwait(false);
        var toAdd = plan.ToAdd.ToList();
        var removed = 0;
        foreach (var slot in plan.ToRemove)
        {
            // The same start with another length: reshape the row rather than delete and insert it.
            var again = toAdd.FindIndex(t => t.StartsAtUtc == slot.StartsAtUtc);
            if (again >= 0 && profile.LicenseVerified)
            {
                slot.ResetTo(toAdd[again].DurationMinutes, clock);
                toAdd.RemoveAt(again);
                continue;
            }

            removed++;
            if (referenced.Contains(slot.Id))
            {
                // A cancelled or moved session still points at this row: take it out of the calendar instead.
                if (slot.Status != SlotStatus.Blocked)
                {
                    slot.Block(clock);
                }
            }
            else
            {
                db.AvailabilitySlots.Remove(slot);
            }
        }

        // An expired or unverified licence stops new times (S19 rule, EC-45); the pattern is still saved.
        var added = 0;
        if (profile.LicenseVerified)
        {
            foreach (var time in toAdd)
            {
                db.AvailabilitySlots.Add(AvailabilitySlot.Create(profile.UserId, time.StartsAtUtc, time.DurationMinutes));
                added++;
            }
        }

        audit.Append("availability.saved", "TherapistAvailability", profile.UserId.ToString(), null, $"added={added};removed={removed};affected={plan.Affected.Count}");
        await SchedulingEndpoints.SaveOrConflictAsync(db, ct).ConfigureAwait(false);
        if (added > 0)
        {
            await SlotOpenedAsync(waitlist, ct).ConfigureAwait(false);
        }

        var dto = await BuildAsync(db, profile, availability, platform.Value, clock, ct).ConfigureAwait(false);
        return TypedResults.Ok(new SaveResultDto(added, removed, plan.Affected.Select(Dto).ToList(), dto));
    }

    /// <summary>A one-off time (S19): the profile's session length, at least two hours ahead, overlapping nothing.</summary>
    internal static async Task<Created<AvailabilitySlotDto>> AddSlotAsync(SlotRequest request, ICurrentUser user, TamaneenDbContext db, WaitlistNotifier waitlist, IAuditLog audit, IOptions<PlatformOptions> platform, IClock clock, CancellationToken ct)
    {
        var (profile, _) = await LoadForWriteAsync(db, user, ct).ConfigureAwait(false);
        var start = CheckStart(request.StartsAt, platform.Value, clock);
        if (!profile.LicenseVerified)
        {
            throw new DomainException(ErrorCodes.SlotUnavailable, "Licence not verified: no new times.");
        }

        var end = start.AddMinutes(profile.SessionMinutes);
        var nearby = await NearbyAsync(db, profile.UserId, start, end, ct).ConfigureAwait(false);
        var blockedHere = nearby.FirstOrDefault(s => s.Status == SlotStatus.Blocked && s.StartsAtUtc == start);
        if (nearby.Any(s => s.Status != SlotStatus.Blocked && s.Overlaps(start, end)))
        {
            throw new DomainException(ErrorCodes.SlotOverlap, "Overlaps another time.");
        }

        AvailabilitySlot slot;
        if (blockedHere is not null)
        {
            blockedHere.Reopen(profile.SessionMinutes);
            slot = blockedHere;
        }
        else
        {
            slot = AvailabilitySlot.Create(profile.UserId, start, profile.SessionMinutes, SlotSource.Manual);
            db.AvailabilitySlots.Add(slot);
        }

        audit.Append("availability.slot_added", "AvailabilitySlot", slot.Id.ToString());
        await SchedulingEndpoints.SaveOrConflictAsync(db, ct).ConfigureAwait(false);
        await SlotOpenedAsync(waitlist, ct).ConfigureAwait(false);
        return TypedResults.Created($"/v1/staff/availability/slots/{slot.Id}", Dto(slot));
    }

    /// <summary>
    /// Moves a free time. A generated time leaves a blocked marker where it was, so the next save of the pattern
    /// does not put it back; the moved time becomes a one-off.
    /// </summary>
    internal static async Task<Ok<AvailabilitySlotDto>> MoveSlotAsync(Guid slotId, SlotRequest request, ICurrentUser user, TamaneenDbContext db, WaitlistNotifier waitlist, IAuditLog audit, IOptions<PlatformOptions> platform, IClock clock, CancellationToken ct)
    {
        var (profile, _) = await LoadForWriteAsync(db, user, ct).ConfigureAwait(false);
        var slot = await OwnSlotAsync(db, profile.UserId, slotId, ct).ConfigureAwait(false);
        if (slot.IsTaken(clock))
        {
            throw new DomainException(ErrorCodes.SlotTaken, "Booked or held.");
        }

        var start = CheckStart(request.StartsAt, platform.Value, clock);
        var end = start.AddMinutes(profile.SessionMinutes);
        var nearby = await NearbyAsync(db, profile.UserId, start, end, ct).ConfigureAwait(false);
        if (nearby.Any(s => s.Id != slot.Id && s.Status != SlotStatus.Blocked && s.Overlaps(start, end)))
        {
            throw new DomainException(ErrorCodes.SlotOverlap, "Overlaps another time.");
        }

        // An old marker exactly where the time goes is no longer needed (and would clash with the unique start).
        foreach (var marker in nearby.Where(s => s.Id != slot.Id && s.Status == SlotStatus.Blocked && s.StartsAtUtc == start))
        {
            var referenced = await db.Appointments.AnyAsync(a => a.SlotId == marker.Id, ct).ConfigureAwait(false);
            if (referenced)
            {
                throw new DomainException(ErrorCodes.SlotOverlap, "A past session holds this start.");
            }

            db.AvailabilitySlots.Remove(marker);
        }

        var wasTemplate = slot.Source == SlotSource.Template && slot.Status != SlotStatus.Blocked;
        var oldStart = slot.StartsAtUtc;
        var oldLength = slot.DurationMinutes;
        var wasBlocked = slot.Status == SlotStatus.Blocked;
        slot.MoveTo(start, profile.SessionMinutes, clock);
        if (wasBlocked)
        {
            slot.Reopen(profile.SessionMinutes);
        }

        if (wasTemplate && oldStart != start)
        {
            var marker = AvailabilitySlot.Create(profile.UserId, oldStart, oldLength);
            marker.Block(clock);
            db.AvailabilitySlots.Add(marker);
        }

        audit.Append("availability.slot_moved", "AvailabilitySlot", slot.Id.ToString());
        await SchedulingEndpoints.SaveOrConflictAsync(db, ct).ConfigureAwait(false);
        await SlotOpenedAsync(waitlist, ct).ConfigureAwait(false);
        return TypedResults.Ok(Dto(slot));
    }

    /// <summary>Removes a free time: a one-off is deleted, a generated one is blocked so the pattern does not bring it back.</summary>
    internal static async Task<NoContent> RemoveSlotAsync(Guid slotId, ICurrentUser user, TamaneenDbContext db, IAuditLog audit, IClock clock, CancellationToken ct)
    {
        var (profile, _) = await LoadForWriteAsync(db, user, ct).ConfigureAwait(false);
        var slot = await OwnSlotAsync(db, profile.UserId, slotId, ct).ConfigureAwait(false);
        if (slot.IsTaken(clock))
        {
            throw new DomainException(ErrorCodes.SlotTaken, "Booked or held.");
        }

        var referenced = await db.Appointments.AnyAsync(a => a.SlotId == slot.Id, ct).ConfigureAwait(false);
        if (slot.Source == SlotSource.Manual && !referenced)
        {
            db.AvailabilitySlots.Remove(slot);
        }
        else if (slot.Status != SlotStatus.Blocked)
        {
            slot.Block(clock);
        }

        audit.Append("availability.slot_removed", "AvailabilitySlot", slot.Id.ToString());
        await SchedulingEndpoints.SaveOrConflictAsync(db, ct).ConfigureAwait(false);
        return TypedResults.NoContent();
    }

    // ---------- helpers ----------

    /// <summary>
    /// <c>availability.slot_opened</c>: internal only. A new or reopened time may be what someone on the waitlist
    /// is waiting for; the waitlist check notifies them itself.
    /// </summary>
    internal static Task SlotOpenedAsync(WaitlistNotifier waitlist, CancellationToken ct) => waitlist.CheckAsync(ct);

    private static async Task<(TherapistProfile Profile, TherapistAvailability Availability)> LoadForWriteAsync(TamaneenDbContext db, ICurrentUser user, CancellationToken ct)
    {
        var me = user.UserId!.Value;
        var profile = await db.TherapistProfiles.FirstOrDefaultAsync(t => t.UserId == me, ct).ConfigureAwait(false)
            ?? throw new DomainException(ErrorCodes.NotFound, "Not a therapist.");
        var availability = await db.TherapistAvailabilities.FirstOrDefaultAsync(a => a.TherapistUserId == me, ct).ConfigureAwait(false);
        if (availability is null)
        {
            availability = TherapistAvailability.Empty(me);
            db.TherapistAvailabilities.Add(availability);
        }

        return (profile, availability);
    }

    private static (List<WeeklyPeriod> Weekly, List<AvailabilityBreak> Breaks, int Session, int Buffer) Parse(SaveAvailabilityRequest request, TherapistProfile profile, TherapistAvailability current)
    {
        var session = request.SessionMinutes ?? profile.SessionMinutes;
        if (!TherapistProfile.SessionLengths.Contains(session))
        {
            throw MeEndpoints.Invalid("sessionMinutes");
        }

        var weekly = (request.Weekly ?? []).Select(p => new WeeklyPeriod(p.Day, p.Start ?? string.Empty, p.End ?? string.Empty)).ToList();
        var breaks = (request.Exceptions ?? []).Select(e => new AvailabilityBreak(e.Id ?? Guid.NewGuid(), e.Kind ?? string.Empty, e.StartsAt.ToUniversalTime(), e.EndsAt.ToUniversalTime())).ToList();
        return (weekly, breaks, session, request.BufferMinutes ?? current.BufferMinutes);
    }

    private static async Task<(AvailabilityPlan Plan, HashSet<Guid> Referenced)> PlanAsync(TamaneenDbContext db, TherapistProfile profile, IReadOnlyList<WeeklyPeriod> weekly, IReadOnlyList<AvailabilityBreak> breaks, int session, int buffer, PlatformOptions platform, IClock clock, CancellationToken ct)
    {
        var zone = Zone(platform);
        var now = clock.UtcNow;
        var desired = AvailabilityPlanner.Generate(weekly, breaks, session, buffer, zone, now, TherapistDirectory.MinimumNotice);
        var earliest = now + TherapistDirectory.MinimumNotice;
        var end = AvailabilityPlanner.HorizonEnd(zone, now);
        var existing = await db.AvailabilitySlots.Where(s => s.TherapistUserId == profile.UserId && s.StartsAtUtc > earliest && s.StartsAtUtc < end).ToListAsync(ct).ConfigureAwait(false);
        var ids = existing.Select(s => s.Id).ToList();
        var referenced = (await db.Appointments.Where(a => ids.Contains(a.SlotId)).Select(a => a.SlotId).ToListAsync(ct).ConfigureAwait(false)).ToHashSet();
        return (AvailabilityPlanner.Reconcile(existing, desired, breaks, clock), referenced);
    }

    private static async Task<AvailabilityDto> BuildAsync(TamaneenDbContext db, TherapistProfile profile, TherapistAvailability availability, PlatformOptions platform, IClock clock, CancellationToken ct)
    {
        var zone = Zone(platform);
        var now = clock.UtcNow;
        var end = AvailabilityPlanner.HorizonEnd(zone, now);
        var slots = await db.AvailabilitySlots.AsNoTracking().Where(s => s.TherapistUserId == profile.UserId && s.StartsAtUtc > now && s.StartsAtUtc < end)
            .OrderBy(s => s.StartsAtUtc).ToListAsync(ct).ConfigureAwait(false);
        var weekly = availability.Weekly;
        var breaks = availability.Exceptions;
        var desired = AvailabilityPlanner.Generate(weekly, breaks, profile.SessionMinutes, availability.BufferMinutes, zone, now, TherapistDirectory.MinimumNotice);
        var earliest = now + TherapistDirectory.MinimumNotice;
        var affected = availability.UpdatedAtUtc is null
            ? []
            : AvailabilityPlanner.Reconcile(slots.Where(s => s.StartsAtUtc > earliest).ToList(), desired, breaks, clock).Affected;
        return new AvailabilityDto(
            profile.UserId,
            platform.TimeZone,
            profile.SessionMinutes,
            TherapistProfile.SessionLengths,
            availability.BufferMinutes,
            TherapistAvailability.BufferOptions,
            weekly.Select(p => new PeriodDto(p.Day, p.Start, p.End)).ToList(),
            breaks.Select(b => new BreakDto(b.Id, b.Kind, b.StartsAtUtc, b.EndsAtUtc)).ToList(),
            AvailabilityPlanner.HorizonDays,
            profile.LicenseVerified,
            availability.UpdatedAtUtc,
            slots.Select(Dto).ToList(),
            affected.Select(Dto).ToList());
    }

    private static DateTimeOffset CheckStart(DateTimeOffset? startsAt, PlatformOptions platform, IClock clock)
    {
        if (startsAt is not { } value)
        {
            throw MeEndpoints.Invalid("startsAt");
        }

        var start = value.ToUniversalTime();
        var now = clock.UtcNow;
        if (start <= now + TherapistDirectory.MinimumNotice)
        {
            throw new DomainException(ErrorCodes.SlotTooSoon, "Less than the minimum notice.");
        }

        if (start >= AvailabilityPlanner.HorizonEnd(Zone(platform), now))
        {
            throw MeEndpoints.Invalid("startsAt");
        }

        return start;
    }

    private static async Task<List<AvailabilitySlot>> NearbyAsync(TamaneenDbContext db, Guid therapistId, DateTimeOffset start, DateTimeOffset end, CancellationToken ct)
    {
        // Sessions are at most three hours long, so anything overlapping starts within that window.
        var from = start.AddHours(-3);
        return await db.AvailabilitySlots.Where(s => s.TherapistUserId == therapistId && s.StartsAtUtc > from && s.StartsAtUtc < end).ToListAsync(ct).ConfigureAwait(false);
    }

    private static async Task<AvailabilitySlot> OwnSlotAsync(TamaneenDbContext db, Guid therapistId, Guid slotId, CancellationToken ct) =>
        await db.AvailabilitySlots.FirstOrDefaultAsync(s => s.Id == slotId && s.TherapistUserId == therapistId, ct).ConfigureAwait(false)
            ?? throw new DomainException(ErrorCodes.NotFound, $"Slot {slotId} not found.");

    internal static TimeZoneInfo Zone(PlatformOptions platform) => TimeZoneInfo.FindSystemTimeZoneById(platform.TimeZone);

    internal static AvailabilitySlotDto Dto(AvailabilitySlot s) => new(s.Id, s.StartsAtUtc, s.EndsAtUtc, s.Status switch
    {
        SlotStatus.Open => "open",
        SlotStatus.Held => "held",
        SlotStatus.Booked => "booked",
        _ => "blocked",
    }, s.Source == SlotSource.Manual ? "manual" : "template");
}
