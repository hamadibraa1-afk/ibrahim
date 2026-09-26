using FieldAttendance.Domain.Entities;
using FieldAttendance.Domain.Enums;

namespace FieldAttendance.Domain.Attendance;

/// <summary>One scheduled day as the compliance counter sees it.</summary>
/// <param name="RequiredMinutes">The day's working time: the scheduled window less the break.</param>
public sealed record ComplianceDay(AttendanceStatus Status, int RequiredMinutes, int LateUnexcused, int EarlyUnexcused)
{
    public static ComplianceDay From(AttendanceRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        return new ComplianceDay(record.Status, Math.Max(0, record.ScheduledWindow.Minutes - record.BreakMinutes),
            record.LateUnexcused, record.EarlyUnexcused);
    }
}

/// <param name="Percent">Null when no day has been settled yet, so a new month shows "—" rather than 0% or 100%.</param>
public sealed record ComplianceResult(int CountedDays, int RequiredMinutes, int CompliantMinutes, decimal? Percent);

/// <summary>
/// How much of the required working time was actually honoured, for the days settled so far.
///
/// A day counts once it is over: checked out, or marked absent. Leave days and days not yet
/// reached are not in the calculation at all, so taking leave never lowers the figure.
///
/// The shortfall is the calculator's own unexcused lateness and early departure, the same
/// figures the deduction proposals are built from, so the percentage can never disagree with
/// what payroll is told. An approved permission is therefore compliant time, and extra hours on
/// one day never cover a shortfall on another: each day is capped at 100%.
/// </summary>
public static class ComplianceCalculator
{
    public static ComplianceResult Calculate(IEnumerable<ComplianceDay> days)
    {
        ArgumentNullException.ThrowIfNull(days);

        var counted = 0;
        var required = 0;
        var compliant = 0;
        foreach (var day in days)
        {
            if (day.Status is not (AttendanceStatus.CheckedOut or AttendanceStatus.Absent) || day.RequiredMinutes <= 0)
                continue;

            counted++;
            required += day.RequiredMinutes;
            if (day.Status == AttendanceStatus.CheckedOut)
                compliant += Math.Clamp(day.RequiredMinutes - day.LateUnexcused - day.EarlyUnexcused, 0, day.RequiredMinutes);
        }

        decimal? percent = required == 0 ? null : Math.Round(100m * compliant / required, 1, MidpointRounding.AwayFromZero);
        return new ComplianceResult(counted, required, compliant, percent);
    }
}
