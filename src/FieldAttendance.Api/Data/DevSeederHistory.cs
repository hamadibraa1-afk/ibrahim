using FieldAttendance.Domain.Attendance;
using FieldAttendance.Domain.Entities;
using FieldAttendance.Domain.Enums;
using FieldAttendance.Domain.Geo;
using FieldAttendance.Domain.Scheduling;
using FieldAttendance.Domain.Time;

namespace FieldAttendance.Api.Data;

/// <param name="Branch">Where they work; the check-in position is taken from here.</param>
internal sealed record HistoryTarget(Guid Employee, Location Branch, ShiftTemplate Shift, WorkSchedule Schedule);

/// <param name="Balances">One per employee and leave type actually used, already reduced by the approved days.</param>
internal sealed record HistoryResult(
    List<AttendanceRecord> Records,
    List<PermissionRequest> Permissions,
    List<LeaveRequest> Leaves,
    List<LeaveBalance> Balances);

/// <summary>
/// Builds a believable past: punches day by day, and the paperwork that explains them.
///
/// The point is not volume. A late arrival with an approved permission behind it and a late
/// arrival without one look identical in a list of punches, yet one costs the employee money
/// and the other does not. This generator produces both, wires each permission to the punch it
/// excuses, and runs every day through the same calculator production uses — so the reports show
/// excused and unexcused minutes the way they will on real data, and the deduction scan raises a
/// proposal for exactly one of the two.
///
/// The randomness is seeded: a reset reproduces the same history, so a figure you questioned
/// yesterday is still there today.
/// </summary>
internal static class DevSeederHistory
{
    /// <summary>Roughly a quarter: three payroll cycles and enough days to see a trend.</summary>
    private const int Days = 80;

    // Bands out of 100, applied to a working day that is not already leave.
    private const int OnTime = 78;
    private const int LateExcused = 85;      // late, with an approved permission — not held against them
    private const int LateUnexcused = 92;    // late, nothing filed — this is what costs money
    private const int EarlyExcused = 95;     // left early, with permission
    private const int EarlyUnexcused = 97;   // left early, nothing filed
    // Above that: absence, about 3% of days.

    public static HistoryResult Build(IReadOnlyList<HistoryTarget> targets, IReadOnlyList<LeaveType> leaveTypes,
        Guid approverId, DateOnly today, DateTimeOffset now, int seed)
    {
        ArgumentNullException.ThrowIfNull(targets);
        ArgumentNullException.ThrowIfNull(leaveTypes);

        var random = new Random(seed);
        var result = new HistoryResult([], [], [], []);

        foreach (var target in targets)
        {
            // Some people are simply more punctual; without this every report ranks identically.
            var discipline = random.Next(-5, 9);
            var workingDays = WorkingDays(target, today);
            var onLeave = PlanLeave(target, workingDays, leaveTypes, approverId, now, random, result);

            foreach (var date in workingDays)
            {
                var day = target.Schedule.For(date.DayOfWeek)!;
                var window = ShiftTiming.Window(date, day.StartTime, day.EndTime);
                var record = new AttendanceRecord(new ResolvedShift(target.Employee, date, target.Branch.Id,
                    target.Shift.Id, window, day.BreakMinutes, target.Schedule.GraceMinutes,
                    target.Schedule.CountEarlyArrivalAsOvertime, target.Schedule.EarlyCheckInMinutes,
                    ShiftSource.BaseAssignment, Guid.NewGuid(), null, false));

                if (onLeave.Contains(date)) record.MarkOnLeave();
                else FillDay(record, window, date, target, random, discipline, now, approverId, result);

                result.Records.Add(record);
            }
        }

        return result;
    }

    private static List<DateOnly> WorkingDays(HistoryTarget target, DateOnly today)
    {
        var days = new List<DateOnly>();
        for (var offset = Days; offset >= 1; offset--)
        {
            var date = today.AddDays(-offset);
            if (target.Schedule.For(date.DayOfWeek) is not null) days.Add(date);
        }
        return days;
    }

    /// <summary>
    /// Gives most people one spell of leave in the quarter, and a few an unpaid one so the
    /// payroll run has a real deduction to show that is not a penalty.
    /// </summary>
    private static HashSet<DateOnly> PlanLeave(HistoryTarget target, List<DateOnly> workingDays,
        IReadOnlyList<LeaveType> leaveTypes, Guid approverId, DateTimeOffset now, Random random, HistoryResult result)
    {
        var taken = new HashSet<DateOnly>();
        if (workingDays.Count < 20 || random.Next(100) >= 62) return taken;

        var unpaid = random.Next(100) < 18;
        var type = unpaid
            ? leaveTypes.FirstOrDefault(t => t.AnnualBalanceDays is null) ?? leaveTypes[0]
            : leaveTypes[0];

        var length = random.Next(3, 7);
        var start = random.Next(5, workingDays.Count - length - 2);
        var days = workingDays.Skip(start).Take(length).ToList();
        foreach (var date in days) taken.Add(date);

        var leave = new LeaveRequest(target.Employee, type.Id, days[0], days[^1], days.Count,
            unpaid ? "إجازة بدون راتب بطلب من الموظف" : "إجازة سنوية", target.Employee);
        leave.Approve(approverId, now.AddDays(-random.Next(20, 60)));
        result.Leaves.Add(leave);

        if (type.AnnualBalanceDays is { } total)
        {
            var balance = new LeaveBalance(target.Employee, type.Id, days[0].Year, total);
            balance.Deduct(days.Count);
            result.Balances.Add(balance);
        }

        return taken;
    }

    private static void FillDay(AttendanceRecord record, TimeInterval window, DateOnly date, HistoryTarget target,
        Random random, int discipline, DateTimeOffset now, Guid approverId, HistoryResult result)
    {
        var roll = random.Next(100) + discipline;
        if (roll >= EarlyUnexcused)
        {
            // Nobody came and nothing was filed: the day stands as an absence.
            record.MarkAbsentIfDue(window.End.AddHours(1), hasPendingException: false);
            return;
        }

        // Always past the grace period, so the excused/unexcused distinction is actually visible.
        // Inside grace there is no lateness at all, and the permission would have nothing to excuse.
        var grace = target.Schedule.GraceMinutes;
        var lateMinutes = roll >= OnTime && roll < LateUnexcused
            ? random.Next(grace + 5, grace + 50)
            : random.Next(-18, 4);
        var earlyMinutes = roll >= LateUnexcused && roll < EarlyUnexcused ? random.Next(25, 75) : 0;
        var overtime = earlyMinutes == 0 && random.Next(100) < 12 ? random.Next(20, 95) : 0;

        var checkIn = window.Start.AddMinutes(lateMinutes);
        var checkOut = window.End.AddMinutes(overtime - earlyMinutes);
        if (checkOut <= checkIn) checkOut = checkIn.AddMinutes(30);

        record.CheckIn(checkIn, NearBranch(target.Branch, random), accuracy: random.Next(6, 28),
            distance: random.Next(0, 40), CheckInType.Normal);
        record.CheckOut(checkOut, NearBranch(target.Branch, random), CheckOutType.Normal);

        // The paperwork, where there is any. A permission covers from the shift start up to the
        // time it allows, so arriving inside it leaves nothing unexcused.
        var approvedLate = new List<TimeInterval>();
        var approvedEarly = new List<TimeInterval>();

        if (roll >= OnTime && roll < LateExcused)
        {
            var until = OrgTime.TimeOf(Clamp(checkIn.AddMinutes(random.Next(5, 20)), window));
            var permission = PermissionRequest.Late(target.Employee, date, until, window,
                Reason(random, late: true), target.Employee);
            permission.Approve(approverId, now.AddDays(-random.Next(5, 70)));
            result.Permissions.Add(permission);
            approvedLate.Add(permission.ToInterval(window));
        }
        else if (roll >= LateUnexcused && roll < EarlyExcused)
        {
            var from = OrgTime.TimeOf(Clamp(checkOut.AddMinutes(-random.Next(5, 20)), window));
            var permission = PermissionRequest.EarlyDeparture(target.Employee, date, from, window,
                Reason(random, late: false), target.Employee);
            permission.Approve(approverId, now.AddDays(-random.Next(5, 70)));
            result.Permissions.Add(permission);
            approvedEarly.Add(permission.ToInterval(window));
        }

        // The calculator production uses, with the approved windows it would have been given.
        record.ApplyCalculation(AttendanceCalculator.Calculate(
            record.BuildCalculationInput(approvedLate, approvedEarly, minimumOvertimeMinutes: 0)));
    }

    /// <summary>
    /// Keeps a permission time strictly inside its shift. The domain rejects anything outside,
    /// and a tuning change to the minute ranges should not be able to break seeding.
    /// </summary>
    private static DateTimeOffset Clamp(DateTimeOffset at, TimeInterval window)
    {
        var first = window.Start.AddMinutes(1);
        var last = window.End.AddMinutes(-1);
        if (at < first) return first;
        return at > last ? last : at;
    }

    private static string Reason(Random random, bool late)
    {
        string[] lateReasons = ["مراجعة طبية", "ازدحام مروري شديد", "ظرف عائلي طارئ", "مهمة رسمية قبل الدوام", "عطل في المركبة"];
        string[] earlyReasons = ["موعد طبي", "ظرف عائلي", "مراجعة جهة حكومية", "استلام أبناء من المدرسة"];
        var pool = late ? lateReasons : earlyReasons;
        return pool[random.Next(pool.Length)];
    }

    /// <summary>A position a few metres from the branch, so distances read like real readings.</summary>
    private static GeoPoint NearBranch(Location branch, Random random)
    {
        const double Jitter = 0.00027; // about ±30 m: inside any sane geofence, never an exact repeat.
        return new GeoPoint(
            branch.Latitude + ((random.NextDouble() - 0.5) * Jitter),
            branch.Longitude + ((random.NextDouble() - 0.5) * Jitter));
    }
}
