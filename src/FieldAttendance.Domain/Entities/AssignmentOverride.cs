using FieldAttendance.Domain.Common;
using FieldAttendance.Domain.Enums;

namespace FieldAttendance.Domain.Entities;

/// <summary>
/// Exception layer over base assignments for a date range.
/// Rule: any override on a date defines the employee's whole day.
/// Cancel = no shift. Other types = exactly the given location + shift
/// (the application pre-fills the shift from the base assignment when the supervisor keeps it).
/// </summary>
public sealed class AssignmentOverride : Entity
{
    private AssignmentOverride() { } // EF Core

    public AssignmentOverride(Guid employeeId, DateOnly fromDate, DateOnly toDate, OverrideType type,
        Guid? locationId, Guid? shiftTemplateId, Guid? replacedEmployeeId, string? reason)
    {
        if (toDate < fromDate)
            throw new DomainException("override.end_before_start", "End date cannot be before start date.");

        EmployeeId = Guard.NotEmpty(employeeId, "override.employee");
        FromDate = fromDate;
        ToDate = toDate;
        Type = type;
        Reason = Guard.Optional(reason, "override.reason", 500);

        if (type == OverrideType.Cancel)
        {
            if (locationId is not null || shiftTemplateId is not null)
                throw new DomainException("override.cancel_has_shift", "A cancel override cannot carry a location or shift.");
        }
        else
        {
            LocationId = Guard.NotEmpty(locationId ?? Guid.Empty, "override.location");
            ShiftTemplateId = Guard.NotEmpty(shiftTemplateId ?? Guid.Empty, "override.shift");
        }

        if (type == OverrideType.Replacement)
        {
            ReplacedEmployeeId = Guard.NotEmpty(replacedEmployeeId ?? Guid.Empty, "override.replaced_employee");
            if (ReplacedEmployeeId == EmployeeId)
                throw new DomainException("override.self_replacement", "An employee cannot replace themselves.");
        }
    }

    public Guid EmployeeId { get; private set; }
    public DateOnly FromDate { get; private set; }
    public DateOnly ToDate { get; private set; }
    public OverrideType Type { get; private set; }
    public Guid? LocationId { get; private set; }
    public Guid? ShiftTemplateId { get; private set; }
    public Guid? ReplacedEmployeeId { get; private set; }
    public string? Reason { get; private set; }

    public bool Covers(DateOnly date) => IsActive && date >= FromDate && date <= ToDate;
}
