using FieldAttendance.Domain.Common;
using FieldAttendance.Domain.Enums;

namespace FieldAttendance.Domain.Entities;

/// <summary>
/// The HR record of an office employee, attached to their login account.
/// Pay lives here as a current figure; every change is also written to
/// <see cref="SalaryChange"/> so the history behind a payslip is never lost.
/// </summary>
public sealed class EmployeeProfile : Entity
{
    private EmployeeProfile() { } // EF Core

    public EmployeeProfile(Guid userId, Guid branchLocationId, Guid departmentId, DateOnly hireDate)
    {
        UserId = Guard.NotEmpty(userId, "profile.user");
        BranchLocationId = Guard.NotEmpty(branchLocationId, "profile.branch");
        DepartmentId = Guard.NotEmpty(departmentId, "profile.department");
        HireDate = hireDate;
    }

    public Guid UserId { get; private set; }
    public Guid BranchLocationId { get; private set; }
    public Guid DepartmentId { get; private set; }
    public Guid? SectionId { get; private set; }
    public Guid? JobTitleId { get; private set; }
    public Guid? GradeId { get; private set; }
    public Guid? ContractTypeId { get; private set; }

    /// <summary>Who approves this person's requests first. Not derived from the department: reality has exceptions.</summary>
    public Guid? ManagerId { get; private set; }

    public Guid? WorkScheduleId { get; private set; }

    public DateOnly HireDate { get; private set; }
    public DateOnly? EndDate { get; private set; }
    public EmploymentStatus Status { get; private set; } = EmploymentStatus.Active;
    public string? EndReason { get; private set; }

    public string? Nationality { get; private set; }
    public DateOnly? BirthDate { get; private set; }
    public string? IdNumber { get; private set; }
    public DateOnly? IdExpiry { get; private set; }
    public string? PassportNumber { get; private set; }
    public DateOnly? PassportExpiry { get; private set; }
    public string? ResidencyNumber { get; private set; }
    public DateOnly? ResidencyExpiry { get; private set; }
    public string? Iban { get; private set; }
    public string? EmergencyContactName { get; private set; }
    public string? EmergencyContactPhone { get; private set; }
    public string? Notes { get; private set; }

    /// <summary>Current monthly basic salary. Changes go through <see cref="ChangeSalary"/>.</summary>
    public decimal BasicSalary { get; private set; }

    public bool IsEmployed => Status != EmploymentStatus.Ended;

    public void SetPlacement(Guid branchLocationId, Guid departmentId, Guid? sectionId, Guid? managerId)
    {
        BranchLocationId = Guard.NotEmpty(branchLocationId, "profile.branch");
        DepartmentId = Guard.NotEmpty(departmentId, "profile.department");
        SectionId = sectionId;
        if (managerId == UserId)
            throw new DomainException("profile.self_manager", "An employee cannot be their own manager.");
        ManagerId = managerId;
    }

    public void SetJob(Guid? jobTitleId, Guid? gradeId, Guid? contractTypeId, DateOnly hireDate)
    {
        JobTitleId = jobTitleId;
        GradeId = gradeId;
        ContractTypeId = contractTypeId;
        if (EndDate is { } end && end < hireDate)
            throw new DomainException("profile.dates", "Hire date cannot be after the end date.");
        HireDate = hireDate;
    }

    public void SetSchedule(Guid? workScheduleId) => WorkScheduleId = workScheduleId;

    public void SetPersonal(string? nationality, DateOnly? birthDate, string? emergencyName, string? emergencyPhone, string? notes)
    {
        Nationality = Guard.Optional(nationality, "profile.nationality", 80);
        BirthDate = birthDate;
        EmergencyContactName = Guard.Optional(emergencyName, "profile.emergency_name", 120);
        EmergencyContactPhone = emergencyPhone is null ? null : User.NormalizePhone(emergencyPhone);
        Notes = Guard.Optional(notes, "profile.notes", 1000);
    }

    public void SetDocuments(string? idNumber, DateOnly? idExpiry, string? passportNumber, DateOnly? passportExpiry,
        string? residencyNumber, DateOnly? residencyExpiry, string? iban)
    {
        IdNumber = Guard.Optional(idNumber, "profile.id_number", 40);
        IdExpiry = idExpiry;
        PassportNumber = Guard.Optional(passportNumber, "profile.passport", 40);
        PassportExpiry = passportExpiry;
        ResidencyNumber = Guard.Optional(residencyNumber, "profile.residency", 40);
        ResidencyExpiry = residencyExpiry;
        Iban = Guard.Optional(iban, "profile.iban", 40)?.Replace(" ", string.Empty, StringComparison.Ordinal).ToUpperInvariant();
    }

    /// <summary>Returns the history entry to store alongside the new figure.</summary>
    public SalaryChange ChangeSalary(decimal newSalary, DateOnly effectiveFrom, string reason, Guid decidedBy)
    {
        if (newSalary < 0 || newSalary > 1_000_000)
            throw new DomainException("profile.salary_range", "Salary is out of range.");
        if (effectiveFrom < HireDate)
            throw new DomainException("profile.salary_before_hire", "Effective date cannot be before the hire date.");

        var change = new SalaryChange(UserId, BasicSalary, newSalary, effectiveFrom, reason, decidedBy);
        BasicSalary = newSalary;
        return change;
    }

    public void Suspend() => Status = Status == EmploymentStatus.Ended
        ? throw new DomainException("profile.ended", "This employee's service has ended.")
        : EmploymentStatus.Suspended;

    public void Reinstate() => Status = Status == EmploymentStatus.Ended
        ? throw new DomainException("profile.ended", "This employee's service has ended.")
        : EmploymentStatus.Active;

    public void EndService(DateOnly endDate, string reason)
    {
        if (endDate < HireDate)
            throw new DomainException("profile.dates", "End date cannot be before the hire date.");
        EndDate = endDate;
        EndReason = Guard.Required(reason, "profile.end_reason", 300);
        Status = EmploymentStatus.Ended;
    }

    /// <summary>Documents expiring within the window, for the HR reminder list.</summary>
    public IReadOnlyList<(string Document, DateOnly Expiry)> ExpiringDocuments(DateOnly today, int withinDays)
    {
        var limit = today.AddDays(withinDays);
        var all = new (string Document, DateOnly? Expiry)[]
        {
            ("id", IdExpiry), ("passport", PassportExpiry), ("residency", ResidencyExpiry),
        };
        return all.Where(d => d.Expiry is { } e && e <= limit)
                  .Select(d => (d.Document, d.Expiry!.Value))
                  .OrderBy(d => d.Item2)
                  .ToList();
    }
}

/// <summary>One salary change: what it was, what it became, from when, why, and who decided.</summary>
public sealed class SalaryChange : Entity
{
    private SalaryChange() { } // EF Core

    internal SalaryChange(Guid employeeId, decimal oldSalary, decimal newSalary, DateOnly effectiveFrom, string reason, Guid decidedBy)
    {
        EmployeeId = employeeId;
        OldSalary = oldSalary;
        NewSalary = newSalary;
        EffectiveFrom = effectiveFrom;
        Reason = Guard.Required(reason, "salary.reason", 300);
        DecidedBy = Guard.NotEmpty(decidedBy, "salary.decided_by");
    }

    public Guid EmployeeId { get; private set; }
    public decimal OldSalary { get; private set; }
    public decimal NewSalary { get; private set; }
    public DateOnly EffectiveFrom { get; private set; }
    public string Reason { get; private set; } = string.Empty;
    public Guid DecidedBy { get; private set; }
}
