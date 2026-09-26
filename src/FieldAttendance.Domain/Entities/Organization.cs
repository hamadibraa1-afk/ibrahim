using FieldAttendance.Domain.Common;

namespace FieldAttendance.Domain.Entities;

/// <summary>
/// Simple configurable lists behind the HR settings screen (job titles, grades, contract types).
/// They exist as entities rather than enums precisely so HR can add and rename them without code.
/// </summary>
public abstract class LookupEntity : Entity
{
    protected LookupEntity() { }

    protected LookupEntity(string nameAr, string nameEn, string? notes) => Rename(nameAr, nameEn, notes);

    public string NameAr { get; private set; } = string.Empty;
    public string NameEn { get; private set; } = string.Empty;
    public string? Notes { get; private set; }
    public int SortOrder { get; private set; }

    public void Rename(string nameAr, string nameEn, string? notes)
    {
        NameAr = Guard.Required(nameAr, "lookup.name_ar", 120);
        NameEn = Guard.Required(nameEn, "lookup.name_en", 120);
        Notes = Guard.Optional(notes, "lookup.notes", 300);
    }

    public void SetSortOrder(int order) => SortOrder = Guard.InRange(order, 0, 9999, "lookup.sort");
}

public sealed class JobTitle : LookupEntity
{
    private JobTitle() { }
    public JobTitle(string nameAr, string nameEn, string? notes = null) : base(nameAr, nameEn, notes) { }
}

public sealed class Grade : LookupEntity
{
    private Grade() { }
    public Grade(string nameAr, string nameEn, string? notes = null) : base(nameAr, nameEn, notes) { }
}

public sealed class ContractType : LookupEntity
{
    private ContractType() { }
    public ContractType(string nameAr, string nameEn, string? notes = null) : base(nameAr, nameEn, notes) { }
}

/// <summary>A department inside the organisation, optionally tied to a branch and led by a manager.</summary>
public sealed class Department : LookupEntity
{
    private Department() { }

    public Department(string nameAr, string nameEn, Guid? branchLocationId, Guid? managerId, string? notes = null)
        : base(nameAr, nameEn, notes)
    {
        BranchLocationId = branchLocationId;
        ManagerId = managerId;
    }

    public Guid? BranchLocationId { get; private set; }
    public Guid? ManagerId { get; private set; }

    public void SetBranch(Guid? branchLocationId) => BranchLocationId = branchLocationId;
    public void SetManager(Guid? managerId) => ManagerId = managerId;
}

/// <summary>A section inside a department, led by a section head. Optional layer.</summary>
public sealed class Section : LookupEntity
{
    private Section() { }

    public Section(Guid departmentId, string nameAr, string nameEn, Guid? headId, string? notes = null)
        : base(nameAr, nameEn, notes)
    {
        DepartmentId = Guard.NotEmpty(departmentId, "section.department");
        HeadId = headId;
    }

    public Guid DepartmentId { get; private set; }
    public Guid? HeadId { get; private set; }

    public void MoveToDepartment(Guid departmentId) => DepartmentId = Guard.NotEmpty(departmentId, "section.department");
    public void SetHead(Guid? headId) => HeadId = headId;
}

/// <summary>A public holiday: a day nobody is expected to attend and that leave does not consume.</summary>
public sealed class Holiday : Entity
{
    private Holiday() { }

    public Holiday(string nameAr, string nameEn, DateOnly fromDate, DateOnly toDate)
    {
        if (toDate < fromDate)
            throw new DomainException("holiday.end_before_start", "End date cannot be before start date.");
        NameAr = Guard.Required(nameAr, "holiday.name_ar", 120);
        NameEn = Guard.Required(nameEn, "holiday.name_en", 120);
        FromDate = fromDate;
        ToDate = toDate;
    }

    public string NameAr { get; private set; } = string.Empty;
    public string NameEn { get; private set; } = string.Empty;
    public DateOnly FromDate { get; private set; }
    public DateOnly ToDate { get; private set; }

    public int Days => ToDate.DayNumber - FromDate.DayNumber + 1;

    public bool Covers(DateOnly date) => IsActive && date >= FromDate && date <= ToDate;
}
