using FieldAttendance.Api.Common;
using FieldAttendance.Api.Data;
using FieldAttendance.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FieldAttendance.Api.Features.Dashboard;

public sealed record DashboardCardsDto(int Scheduled, int PresentNow, int Late, int Absent, int OnLeave, int OnExit,
    int UncoveredLocations, int PendingRequests, double? AverageRating, int RatingCount, int OpenFeedback);

public sealed record BoardEmployeeDto(Guid EmployeeId, string Name, string Phone, string Email, string? EmployeeNumber,
    string State, DateTimeOffset ScheduledStart, DateTimeOffset ScheduledEnd, DateTimeOffset? CheckInAt, int LateMinutes);

public sealed record BoardLocationDto(Guid LocationId, string NameAr, string NameEn, string Color, double Latitude, double Longitude,
    int RadiusMeters, IReadOnlyList<BoardEmployeeDto> Employees);

public sealed record DashboardDto(DashboardCardsDto Cards, IReadOnlyList<BoardLocationDto> Board);

[ApiController]
[Route("api/dashboard")]
[Authorize(Policy = Policies.Read)]
public sealed class DashboardController(AppDbContext db, IClock clock, AccessScope scope, Requests.RequestInbox inbox) : ControllerBase
{
    /// <summary>Live state for today. The Angular page polls this every 30 seconds.</summary>
    [HttpGet]
    public async Task<DashboardDto> Get(CancellationToken ct)
    {
        var now = clock.Now;
        var today = clock.Today;
        var yesterday = today.AddDays(-1);
        var startOfDay = Domain.Time.UaeTime.At(today, TimeOnly.MinValue);

        // The field dashboard is the field sites only. Office branches, and the office staff who work
        // at them, belong to the HR module; showing them here mixed the two workforces on one board.
        var fieldSites = db.Locations.Where(l => l.Kind == LocationKind.Field).Select(l => l.Id);
        var records = await db.AttendanceRecords.AsNoTracking().Include(r => r.Exits)
            .Where(r => fieldSites.Contains(r.LocationId))
            .Where(r => r.ShiftDate == today || (r.ShiftDate == yesterday && (r.ScheduledEnd > now || (r.CheckOutAt == null && r.CheckInAt != null))))
            .ToListAsync(ct);
        var users = await db.Users.AsNoTracking().Where(u => u.Role == UserRole.Collector).ToDictionaryAsync(u => u.Id, ct);
        var scoped = await scope.LocationIdsAsync(ct);
        var locations = await db.Locations.AsNoTracking().Where(l => l.IsActive && l.Kind == LocationKind.Field).OrderBy(l => l.NameAr).ToListAsync(ct);
        if (scoped is not null) locations = locations.Where(l => scoped.Contains(l.Id)).ToList();

        string StateOf(Domain.Entities.AttendanceRecord r) =>
            r.Status == AttendanceStatus.OnLeave ? "OnLeave"
            : r.Status == AttendanceStatus.Absent ? "Absent"
            : r.OpenExit is not null ? "OnExit"
            : r.IsOpen ? (r.LateUnexcused > 0 ? "PresentLate" : "Present")
            : r.CheckOutAt is not null ? "CheckedOut"
            : now > r.LateAfter && now < r.ScheduledEnd ? "NotArrived"
            : "Upcoming";

        var board = locations.Select(l =>
        {
            var here = records.Where(r => r.LocationId == l.Id).OrderBy(r => r.ScheduledStart).ToList();
            var employees = here.Select(r =>
            {
                var u = users.GetValueOrDefault(r.EmployeeId);
                var lateNow = r.CheckInAt is null && now > r.ScheduledStart && now < r.ScheduledEnd
                    ? (int)(now - r.ScheduledStart).TotalMinutes : r.LateUnexcused;
                return new BoardEmployeeDto(r.EmployeeId, u?.FullName ?? "?", u?.Phone ?? "", u?.Email ?? "", u?.EmployeeNumber,
                    StateOf(r), r.ScheduledStart, r.ScheduledEnd, r.CheckInAt, lateNow);
            }).ToList();

            var active = here.Where(r => r.ScheduledStart <= now && now < r.ScheduledEnd).ToList();
            var states = employees.Where(e => e.ScheduledStart <= now && now < e.ScheduledEnd).Select(e => e.State).ToList();
            var color = active.Count == 0 ? (here.Count == 0 ? "red" : "grey")
                : states.Any(s => s is "Present" or "PresentLate") ? (states.Any(s => s is "PresentLate" or "OnExit" or "NotArrived") ? "yellow" : "green")
                : states.Any(s => s == "OnExit") ? "yellow"
                : "red";
            return new BoardLocationDto(l.Id, l.NameAr, l.NameEn, color, l.Latitude, l.Longitude, l.RadiusMeters, employees);
        }).ToList();

        var ratingsToday = await db.Ratings.AsNoTracking()
            .Where(r => r.IncludedInAverage && r.ScannedAt >= startOfDay)
            .Select(r => r.Stars).ToListAsync(ct);
        // What the viewer has to act on, the same figure as the badge; it used to count every
        // pending request in the organisation, office ones included.
        var counts = await inbox.CountsAsync(User, ct);
        var pending = counts.Permissions + counts.Exceptions + counts.Leaves;
        var allStates = board.SelectMany(b => b.Employees).ToList();

        var cards = new DashboardCardsDto(
            Scheduled: records.Count(r => r.ShiftDate == today),
            PresentNow: allStates.Count(e => e.State is "Present" or "PresentLate"),
            Late: allStates.Count(e => e.State is "PresentLate" or "NotArrived"),
            Absent: allStates.Count(e => e.State == "Absent"),
            OnLeave: allStates.Count(e => e.State == "OnLeave"),
            OnExit: allStates.Count(e => e.State == "OnExit"),
            UncoveredLocations: board.Count(b => b.Color == "red"),
            PendingRequests: pending,
            AverageRating: ratingsToday.Count > 0 ? Math.Round(ratingsToday.Average(), 2) : null,
            RatingCount: ratingsToday.Count,
            OpenFeedback: await db.Feedback.CountAsync(f => f.IsActive && f.Status != FeedbackStatus.Closed, ct));

        return new DashboardDto(cards, board);
    }
}
