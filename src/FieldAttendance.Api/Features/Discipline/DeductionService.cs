using FieldAttendance.Api.Common;
using FieldAttendance.Api.Data;
using FieldAttendance.Domain.Common;
using FieldAttendance.Domain.Entities;
using FieldAttendance.Domain.Enums;
using FieldAttendance.Domain.Payroll;
using Microsoft.EntityFrameworkCore;

namespace FieldAttendance.Api.Features.Discipline;

/// <summary>
/// Watches attendance and proposes deductions. It never takes money: a proposal waits for a
/// person, who may approve it, reduce it, replace it with a warning, or cancel it.
/// Scanning is idempotent — the same day can be scanned repeatedly without duplicating anything.
/// </summary>
public sealed class DeductionService(AppDbContext db, PayrollLock payrollLock, Payroll.SalaryLookup salaries)
{
    public async Task<int> ScanAsync(DateOnly from, DateOnly to, Guid? employeeId, CancellationToken ct)
    {
        if (to < from) throw new DomainException("deduction.range", "End date cannot be before the start date.");
        await payrollLock.EnsureRangeOpenAsync(from, to, ct);

        var types = await db.DeductionTypes.AsNoTracking()
            .Where(t => t.IsActive && t.Trigger != DeductionTrigger.Manual).ToListAsync(ct);
        if (types.Count == 0) return 0;

        var records = await db.AttendanceRecords.AsNoTracking()
            .Where(r => r.ShiftDate >= from && r.ShiftDate <= to)
            .Where(r => employeeId == null || r.EmployeeId == employeeId)
            .ToListAsync(ct);
        if (records.Count == 0) return 0;

        // Anything already proposed, approved or turned into a warning must not come back.
        var existing = await db.DeductionProposals.AsNoTracking()
            .Where(p => p.IsActive && p.OnDate >= from && p.OnDate <= to)
            .Select(p => new { p.EmployeeId, p.DeductionTypeId, p.AttendanceRecordId })
            .ToListAsync(ct);
        var seen = existing.Select(e => (e.EmployeeId, e.DeductionTypeId, e.AttendanceRecordId)).ToHashSet();

        var created = 0;
        foreach (var type in types)
        {
            var matches = records.Where(r => Matches(type.Trigger, r)).OrderBy(r => r.ShiftDate).ToList();

            foreach (var group in matches.GroupBy(r => r.EmployeeId))
            {
                var occurrences = 0;
                foreach (var record in group)
                {
                    occurrences++;
                    if (type.TriggerThreshold is { } threshold && occurrences < threshold) continue;
                    if (!seen.Add((record.EmployeeId, type.Id, record.Id))) continue;

                    db.DeductionProposals.Add(new DeductionProposal(record.EmployeeId, type.Id, record.ShiftDate,
                        UnitsFor(type, record), Describe(type.Trigger, record), record.Id, null));
                    created++;
                }
            }
        }

        await db.SaveChangesAsync(ct);
        return created;
    }

    /// <summary>Money value of a proposal, from the salary that employee had on the day it concerns.</summary>
    public async Task<decimal> AmountAsync(DeductionProposal proposal, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(proposal);
        var type = await db.DeductionTypes.AsNoTracking().SingleAsync(t => t.Id == proposal.DeductionTypeId, ct);
        var profile = await db.EmployeeProfiles.AsNoTracking()
            .SingleOrDefaultAsync(p => p.UserId == proposal.EmployeeId, ct);
        var salary = profile is null ? 0m : await salaries.OnAsync(profile, proposal.OnDate, ct);
        var policy = await PolicyAsync(ct);
        return PayrollMath.DeductionAmount(salary, policy, type.Unit, proposal.Units);
    }

    public async Task<PayrollPolicy> PolicyAsync(CancellationToken ct)
    {
        var settings = await db.SystemSettings.AsNoTracking().ToDictionaryAsync(s => s.Key, s => s.Value, ct);
        return new PayrollPolicy(
            Read(settings, "payroll.monthDays", 30),
            Read(settings, "payroll.dailyWorkMinutes", 480),
            Read(settings, "payroll.overtimeFactor", 1.25m),
            Read(settings, "payroll.maxDeductionPercent", 25m));
    }

    private static bool Matches(DeductionTrigger trigger, AttendanceRecord r) => trigger switch
    {
        DeductionTrigger.Late => r.LateUnexcused > 0,
        DeductionTrigger.Absence => r.Status == AttendanceStatus.Absent,
        DeductionTrigger.EarlyDeparture => r.EarlyUnexcused > 0,
        DeductionTrigger.MissingCheckOut => r.CheckOutType == CheckOutType.Auto,
        _ => false,
    };

    /// <summary>A day-based type charges its own amount; an hour-based one charges the real minutes lost.</summary>
    private static decimal UnitsFor(DeductionType type, AttendanceRecord r)
    {
        if (type.Unit != DeductionUnit.Hour) return type.Amount;
        var minutes = type.Trigger switch
        {
            DeductionTrigger.Late => r.LateUnexcused,
            DeductionTrigger.EarlyDeparture => r.EarlyUnexcused,
            _ => 0,
        };
        return minutes > 0 ? decimal.Round(minutes / 60m, 2) : type.Amount;
    }

    private static string Describe(DeductionTrigger trigger, AttendanceRecord r) => trigger switch
    {
        DeductionTrigger.Late => $"تأخير غير مبرر {r.LateUnexcused} دقيقة",
        DeductionTrigger.Absence => "غياب بدون إذن",
        DeductionTrigger.EarlyDeparture => $"انصراف مبكر غير مبرر {r.EarlyUnexcused} دقيقة",
        _ => "عدم تسجيل الانصراف",
    };

    private static int Read(Dictionary<string, string> settings, string key, int fallback) =>
        settings.TryGetValue(key, out var value) && int.TryParse(value, out var parsed) ? parsed : fallback;

    private static decimal Read(Dictionary<string, string> settings, string key, decimal fallback) =>
        settings.TryGetValue(key, out var value) && decimal.TryParse(value, out var parsed) ? parsed : fallback;
}
