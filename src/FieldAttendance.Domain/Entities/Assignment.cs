using FieldAttendance.Domain.Common;
using FieldAttendance.Domain.Enums;

namespace FieldAttendance.Domain.Entities;

/// <summary>Base assignment: employee + location + shift, repeating on <see cref="Days"/> within a date range.</summary>
public sealed class Assignment : Entity
{
    private Assignment() { } // EF Core

    public Assignment(Guid employeeId, Guid locationId, Guid shiftTemplateId, DateOnly startDate, DateOnly? endDate, WorkDays days, string? notes)
    {
        EmployeeId = Guard.NotEmpty(employeeId, "assignment.employee");
        LocationId = Guard.NotEmpty(locationId, "assignment.location");
        ShiftTemplateId = Guard.NotEmpty(shiftTemplateId, "assignment.shift");
        StartDate = startDate;
        SetEndDate(endDate);
        Days = days == WorkDays.None
            ? throw new DomainException("assignment.no_days", "At least one working day is required.")
            : days;
        Notes = Guard.Optional(notes, "assignment.notes", 500);
    }

    public Guid EmployeeId { get; private set; }
    public Guid LocationId { get; private set; }
    public Guid ShiftTemplateId { get; private set; }
    public DateOnly StartDate { get; private set; }
    public DateOnly? EndDate { get; private set; }
    public WorkDays Days { get; private set; }
    public string? Notes { get; private set; }

    public bool Covers(DateOnly date) =>
        IsActive && date >= StartDate && (EndDate is null || date <= EndDate) && Days.Includes(date.DayOfWeek);

    /// <summary>Extend (or open-end with null). Used by the "Extend" button.</summary>
    public void SetEndDate(DateOnly? endDate)
    {
        if (endDate is { } end && end < StartDate)
            throw new DomainException("assignment.end_before_start", "End date cannot be before start date.");
        EndDate = endDate;
    }

    /// <summary>Ends the assignment the day before <paramref name="effectiveDate"/> (permanent transfer / unassign).</summary>
    public void EndBefore(DateOnly effectiveDate)
    {
        if (effectiveDate <= StartDate)
        {
            Deactivate(); // never took effect
            return;
        }
        SetEndDate(effectiveDate.AddDays(-1));
    }
}
