using FieldAttendance.Domain.Common;
using FieldAttendance.Domain.Time;

namespace FieldAttendance.Domain.Entities;

public sealed class ShiftTemplate : Entity
{
    private ShiftTemplate() { } // EF Core

    public ShiftTemplate(string nameAr, string nameEn, TimeOnly start, TimeOnly end, int breakMinutes, int graceMinutes,
        bool countEarlyArrivalAsOvertime, int earlyCheckInMinutes = 0)
    {
        Rename(nameAr, nameEn);
        SetTimes(start, end, breakMinutes);
        SetRules(graceMinutes, countEarlyArrivalAsOvertime, earlyCheckInMinutes);
    }

    public string NameAr { get; private set; } = string.Empty;
    public string NameEn { get; private set; } = string.Empty;
    public TimeOnly StartTime { get; private set; }
    public TimeOnly EndTime { get; private set; }
    public int BreakMinutes { get; private set; }
    public int GraceMinutes { get; private set; }
    public bool CountEarlyArrivalAsOvertime { get; private set; }

    /// <summary>How long before the shift starts an employee may check in. 0 = only from the start time.</summary>
    public int EarlyCheckInMinutes { get; private set; }

    public bool CrossesMidnight => EndTime <= StartTime;

    /// <summary>TimeOnly subtraction wraps past midnight, so 22:00 → 06:00 is 480 minutes.</summary>
    public int DurationMinutes => (int)(EndTime - StartTime).TotalMinutes;

    public void Rename(string nameAr, string nameEn)
    {
        NameAr = Guard.Required(nameAr, "shift.name_ar", 100);
        NameEn = Guard.Required(nameEn, "shift.name_en", 100);
    }

    /// <summary>
    /// Changing times affects only records materialized after the change;
    /// existing attendance records keep their own snapshot of the schedule.
    /// </summary>
    public void SetTimes(TimeOnly start, TimeOnly end, int breakMinutes)
    {
        if (start == end)
            throw new DomainException("shift.zero_length", "Shift start and end cannot be equal.");
        StartTime = start;
        EndTime = end;
        BreakMinutes = Guard.InRange(breakMinutes, 0, DurationMinutes - 1, "shift.break");
    }

    public void SetRules(int graceMinutes, bool countEarlyArrivalAsOvertime, int earlyCheckInMinutes = 0)
    {
        GraceMinutes = Guard.InRange(graceMinutes, 0, 120, "shift.grace");
        CountEarlyArrivalAsOvertime = countEarlyArrivalAsOvertime;
        EarlyCheckInMinutes = Guard.InRange(earlyCheckInMinutes, 0, 240, "shift.early_check_in");
    }

    public TimeInterval WindowFor(DateOnly shiftDate) => ShiftTiming.Window(shiftDate, StartTime, EndTime);
}
