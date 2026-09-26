using FieldAttendance.Domain.Common;
using FieldAttendance.Domain.Enums;

namespace FieldAttendance.Domain.Entities;

/// <summary>
/// A fixed weekly pattern for office staff: which days they work and at what times.
/// Days may differ (a shorter Friday, for example). Rules that the attendance engine
/// already understands — grace, early check-in, overtime — live here too, so office
/// attendance is computed by exactly the same engine as the field.
/// </summary>
public sealed class WorkSchedule : Entity
{
    private readonly List<WorkScheduleDay> _days = [];

    private WorkSchedule() { } // EF Core

    public WorkSchedule(string nameAr, string nameEn, int graceMinutes, int earlyCheckInMinutes, bool countEarlyArrivalAsOvertime)
    {
        Rename(nameAr, nameEn);
        SetRules(graceMinutes, earlyCheckInMinutes, countEarlyArrivalAsOvertime);
    }

    public string NameAr { get; private set; } = string.Empty;
    public string NameEn { get; private set; } = string.Empty;
    public int GraceMinutes { get; private set; }
    public int EarlyCheckInMinutes { get; private set; }
    public bool CountEarlyArrivalAsOvertime { get; private set; }

    public IReadOnlyCollection<WorkScheduleDay> Days => _days.AsReadOnly();

    public WorkDays WorkingDays =>
        _days.Aggregate(WorkDays.None, (mask, day) => mask | (WorkDays)(1 << (int)day.Day));

    public int WeeklyMinutes => _days.Sum(d => d.WorkMinutes);

    public void Rename(string nameAr, string nameEn)
    {
        NameAr = Guard.Required(nameAr, "schedule.name_ar", 120);
        NameEn = Guard.Required(nameEn, "schedule.name_en", 120);
    }

    public void SetRules(int graceMinutes, int earlyCheckInMinutes, bool countEarlyArrivalAsOvertime)
    {
        GraceMinutes = Guard.InRange(graceMinutes, 0, 120, "schedule.grace");
        EarlyCheckInMinutes = Guard.InRange(earlyCheckInMinutes, 0, 240, "schedule.early_check_in");
        CountEarlyArrivalAsOvertime = countEarlyArrivalAsOvertime;
    }

    /// <summary>Adds the day or replaces its times if it is already in the pattern.</summary>
    public WorkScheduleDay SetDay(DayOfWeek day, TimeOnly start, TimeOnly end, int breakMinutes)
    {
        var existing = _days.SingleOrDefault(d => d.Day == day);
        if (existing is not null)
        {
            existing.SetTimes(start, end, breakMinutes);
            return existing;
        }

        var created = new WorkScheduleDay(Id, day, start, end, breakMinutes);
        _days.Add(created);
        return created;
    }

    public void RemoveDay(DayOfWeek day)
    {
        var existing = _days.SingleOrDefault(d => d.Day == day);
        if (existing is not null) _days.Remove(existing);
    }

    public void EnsureUsable()
    {
        if (_days.Count == 0)
            throw new DomainException("schedule.no_days", "Add at least one working day.");
    }

    public WorkScheduleDay? For(DayOfWeek day) => _days.SingleOrDefault(d => d.Day == day);

    /// <summary>Distinct working times in the pattern, each with the days that use it.</summary>
    public IReadOnlyList<(TimeOnly Start, TimeOnly End, int BreakMinutes, WorkDays Days)> TimeGroups() =>
        _days.GroupBy(d => (d.StartTime, d.EndTime, d.BreakMinutes))
             .Select(g => (g.Key.StartTime, g.Key.EndTime, g.Key.BreakMinutes,
                 g.Aggregate(WorkDays.None, (mask, d) => mask | (WorkDays)(1 << (int)d.Day))))
             .OrderBy(g => g.StartTime)
             .ToList();
}

public sealed class WorkScheduleDay : Entity
{
    private WorkScheduleDay() { } // EF Core

    internal WorkScheduleDay(Guid workScheduleId, DayOfWeek day, TimeOnly start, TimeOnly end, int breakMinutes)
    {
        WorkScheduleId = workScheduleId;
        Day = day;
        SetTimes(start, end, breakMinutes);
    }

    public Guid WorkScheduleId { get; private set; }
    public DayOfWeek Day { get; private set; }
    public TimeOnly StartTime { get; private set; }
    public TimeOnly EndTime { get; private set; }
    public int BreakMinutes { get; private set; }

    /// <summary>TimeOnly subtraction wraps past midnight, so a night pattern is handled too.</summary>
    public int DurationMinutes => (int)(EndTime - StartTime).TotalMinutes;

    public int WorkMinutes => Math.Max(0, DurationMinutes - BreakMinutes);

    public void SetTimes(TimeOnly start, TimeOnly end, int breakMinutes)
    {
        if (start == end)
            throw new DomainException("schedule.zero_length", "Start and end time cannot be equal.");
        StartTime = start;
        EndTime = end;
        BreakMinutes = Guard.InRange(breakMinutes, 0, DurationMinutes - 1, "schedule.break");
    }
}
