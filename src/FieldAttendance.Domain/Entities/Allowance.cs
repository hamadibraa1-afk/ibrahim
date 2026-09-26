using FieldAttendance.Domain.Common;

namespace FieldAttendance.Domain.Entities;

/// <summary>A kind of allowance HR grants for a period, e.g. transport or field allowance.</summary>
public sealed class AllowanceType : Entity
{
    private AllowanceType() { } // EF Core

    public AllowanceType(string nameAr, string nameEn, decimal? dailyAmount, string? notes) =>
        Update(nameAr, nameEn, dailyAmount, notes);

    public string NameAr { get; private set; } = string.Empty;
    public string NameEn { get; private set; } = string.Empty;

    /// <summary>Optional daily value, carried into the HR report. Null = the amount is handled outside the system.</summary>
    public decimal? DailyAmount { get; private set; }
    public string? Notes { get; private set; }

    public void Update(string nameAr, string nameEn, decimal? dailyAmount, string? notes)
    {
        NameAr = Guard.Required(nameAr, "allowance_type.name_ar", 100);
        NameEn = Guard.Required(nameEn, "allowance_type.name_en", 100);
        if (dailyAmount is { } amount && (amount < 0 || amount > 100_000))
            throw new DomainException("allowance_type.amount", "Daily amount is out of range.");
        DailyAmount = dailyAmount;
        Notes = Guard.Optional(notes, "allowance_type.notes", 300);
    }
}

/// <summary>
/// An allowance granted to one employee for a period. Scheduled by date range, so it shows
/// on every attendance day it covers and totals up in the HR report.
/// </summary>
public sealed class EmployeeAllowance : Entity
{
    private EmployeeAllowance() { } // EF Core

    public EmployeeAllowance(Guid employeeId, Guid allowanceTypeId, DateOnly fromDate, DateOnly toDate, string? notes)
    {
        if (toDate < fromDate)
            throw new DomainException("allowance.end_before_start", "End date cannot be before start date.");
        EmployeeId = Guard.NotEmpty(employeeId, "allowance.employee");
        AllowanceTypeId = Guard.NotEmpty(allowanceTypeId, "allowance.type");
        FromDate = fromDate;
        ToDate = toDate;
        Notes = Guard.Optional(notes, "allowance.notes", 300);
    }

    public Guid EmployeeId { get; private set; }
    public Guid AllowanceTypeId { get; private set; }
    public DateOnly FromDate { get; private set; }
    public DateOnly ToDate { get; private set; }
    public string? Notes { get; private set; }

    public int Days => ToDate.DayNumber - FromDate.DayNumber + 1;

    public bool Covers(DateOnly date) => IsActive && date >= FromDate && date <= ToDate;

    public bool Overlaps(EmployeeAllowance other) =>
        other.EmployeeId == EmployeeId && other.AllowanceTypeId == AllowanceTypeId
        && FromDate <= other.ToDate && other.FromDate <= ToDate;
}

/// <summary>Links a supervisor to the sites they are responsible for. No links = responsible for all sites.</summary>
public sealed class SupervisorLocation : Entity
{
    private SupervisorLocation() { } // EF Core

    public SupervisorLocation(Guid supervisorId, Guid locationId)
    {
        SupervisorId = Guard.NotEmpty(supervisorId, "supervisor_location.supervisor");
        LocationId = Guard.NotEmpty(locationId, "supervisor_location.location");
    }

    public Guid SupervisorId { get; private set; }
    public Guid LocationId { get; private set; }
}
