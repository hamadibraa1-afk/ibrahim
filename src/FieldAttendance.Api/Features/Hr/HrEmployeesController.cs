using System.ComponentModel.DataAnnotations;
using FieldAttendance.Api.Auth;
using FieldAttendance.Api.Common;
using FieldAttendance.Api.Data;
using FieldAttendance.Domain.Common;
using FieldAttendance.Domain.Entities;
using FieldAttendance.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FieldAttendance.Api.Features.Hr;

public sealed record HrEmployeeRow(Guid Id, string FullName, string EmployeeNumber, string Role, string? Phone,
    Guid DepartmentId, string DepartmentName, string? SectionName, string? JobTitleName, string BranchName,
    string? ManagerName, DateOnly HireDate, string Status, string? ScheduleName, bool IsActive, string Workforce);

public sealed record HrEmployeeDetail(HrEmployeeRow Row, Guid BranchLocationId, Guid? SectionId, Guid? JobTitleId,
    Guid? GradeId, Guid? ContractTypeId, Guid? ManagerId, Guid? WorkScheduleId, string? Email, string? Nationality,
    DateOnly? BirthDate, string? IdNumber, DateOnly? IdExpiry, string? PassportNumber, DateOnly? PassportExpiry,
    string? ResidencyNumber, DateOnly? ResidencyExpiry, string? Iban, string? EmergencyContactName,
    string? EmergencyContactPhone, string? Notes, decimal? BasicSalary, DateOnly? EndDate, string? EndReason,
    byte[] RowVersion);

public sealed record CreateHrEmployeeRequest(
    [Required] string FullName, [Required] string EmployeeNumber, [Required] string Phone, [EmailAddress] string? Email,
    [Required] string Role, [Required] string PreferredLanguage, [Required] string Password,
    Guid BranchLocationId, Guid DepartmentId, Guid? SectionId, Guid? JobTitleId, Guid? GradeId, Guid? ContractTypeId,
    Guid? ManagerId, Guid? WorkScheduleId, DateOnly HireDate, decimal BasicSalary, string? Workforce = null);

public sealed record UpdateHrEmployeeRequest(
    [Required] string FullName, [Required] string Phone, [EmailAddress] string? Email, [Required] string PreferredLanguage,
    Guid BranchLocationId, Guid DepartmentId, Guid? SectionId, Guid? JobTitleId, Guid? GradeId, Guid? ContractTypeId,
    Guid? ManagerId, DateOnly HireDate, string? Nationality, DateOnly? BirthDate,
    string? EmergencyContactName, string? EmergencyContactPhone, string? Notes,
    string? IdNumber, DateOnly? IdExpiry, string? PassportNumber, DateOnly? PassportExpiry,
    string? ResidencyNumber, DateOnly? ResidencyExpiry, string? Iban, byte[]? RowVersion);

public sealed record AssignScheduleRequest(Guid? WorkScheduleId, DateOnly EffectiveFrom);

public sealed record ChangeSalaryRequest([Range(0, 1000000)] decimal NewSalary, DateOnly EffectiveFrom, [Required] string Reason);

public sealed record EndServiceRequest(DateOnly EndDate, [Required] string Reason);

public sealed record SalaryHistoryRow(decimal OldSalary, decimal NewSalary, DateOnly EffectiveFrom, string Reason,
    string? DecidedByName, DateTimeOffset At);

/// <summary>
/// Every employee, office and field: the HR record plus the login account, created together so an
/// employee never exists with one and not the other. HR sees both workforces; the field module
/// rosters field staff to sites, and office staff follow a weekly work schedule.
/// </summary>
[ApiController]
[Route("api/hr/employees")]
[Authorize(Policy = HrPolicies.Read)]
public sealed class HrEmployeesController(AppDbContext db, OfficeScheduleService office, AccessScope scope,
    ICurrentUser me, IClock clock, Attendance.ComplianceService compliance) : ControllerBase
{
    private static readonly UserRole[] OfficeRoles =
        [UserRole.Employee, UserRole.HrOfficer, UserRole.HrManager, UserRole.DepartmentManager, UserRole.SystemAdmin, UserRole.Supervisor];

    [HttpGet]
    public async Task<IReadOnlyList<HrEmployeeRow>> List([FromQuery] Guid? departmentId, [FromQuery] string? status,
        [FromQuery] bool includeEnded, [FromQuery] string? workforce, CancellationToken ct)
    {
        var profiles = await db.EmployeeProfiles.AsNoTracking().ToListAsync(ct);
        if (departmentId is { } d) profiles = profiles.Where(p => p.DepartmentId == d).ToList();
        if (Enum.TryParse<Workforce>(workforce, true, out var side)) profiles = profiles.Where(p => p.Workforce == side).ToList();

        // A department manager's list is their own department, never the whole organisation.
        if (!scope.SeesEveryone && scope.IsDepartmentManager && me.Id is { } managerId)
        {
            var managed = await db.Departments.AsNoTracking()
                .Where(x => x.ManagerId == managerId).Select(x => x.Id).ToListAsync(ct);
            profiles = profiles.Where(p => managed.Contains(p.DepartmentId) || p.ManagerId == managerId || p.UserId == managerId).ToList();
        }
        if (Enum.TryParse<EmploymentStatus>(status, true, out var parsed))
            profiles = profiles.Where(p => p.Status == parsed).ToList();
        else if (!includeEnded)
            profiles = profiles.Where(p => p.Status != EmploymentStatus.Ended).ToList();

        var context = await LoadContextAsync(ct);
        return profiles.Select(p => ToRow(p, context)).OrderBy(r => r.FullName).ToList();
    }

    [HttpGet("{id:guid}")]
    public async Task<HrEmployeeDetail> Get(Guid id, CancellationToken ct)
    {
        var profile = await Find(id, ct);
        await scope.EnsureCanSeeEmployeeAsync(profile.UserId, ct);
        var context = await LoadContextAsync(ct);
        var user = context.Users[profile.UserId];
        var showSalary = User.IsInRole(nameof(UserRole.SystemAdmin)) || User.IsInRole(nameof(UserRole.HrManager))
                         || me.Id == profile.UserId;

        return new HrEmployeeDetail(ToRow(profile, context), profile.BranchLocationId, profile.SectionId, profile.JobTitleId,
            profile.GradeId, profile.ContractTypeId, profile.ManagerId, profile.WorkScheduleId, user.Email,
            profile.Nationality, profile.BirthDate, profile.IdNumber, profile.IdExpiry, profile.PassportNumber,
            profile.PassportExpiry, profile.ResidencyNumber, profile.ResidencyExpiry, profile.Iban,
            profile.EmergencyContactName, profile.EmergencyContactPhone, profile.Notes,
            showSalary ? profile.BasicSalary : null, profile.EndDate, profile.EndReason, profile.RowVersion);
    }

    [HttpPost]
    [Authorize(Policy = HrPolicies.Manage)]
    public async Task<IActionResult> Create(CreateHrEmployeeRequest r, CancellationToken ct)
    {
        var workforce = Enum.TryParse<Workforce>(r.Workforce, true, out var w) ? w : Workforce.Office;
        var role = ParseRole(r.Role, workforce);
        PasswordHasher.EnsureStrong(r.Password);
        await EnsureReferencesAsync(r.BranchLocationId, r.DepartmentId, r.SectionId, r.WorkScheduleId, r.ManagerId, ct);

        var user = new User(r.FullName, r.Email, r.Phone, role, r.EmployeeNumber, r.PreferredLanguage);
        user.SetPasswordHash(PasswordHasher.Hash(r.Password));
        db.Users.Add(user);

        var profile = new EmployeeProfile(user.Id, r.BranchLocationId, r.DepartmentId, r.HireDate, workforce);
        profile.SetPlacement(r.BranchLocationId, r.DepartmentId, r.SectionId, r.ManagerId);
        profile.SetJob(r.JobTitleId, r.GradeId, r.ContractTypeId, r.HireDate);
        profile.SetSchedule(r.WorkScheduleId);
        if (r.BasicSalary > 0)
            db.SalaryChanges.Add(profile.ChangeSalary(r.BasicSalary, r.HireDate, "الراتب عند التعيين", me.RequiredId));
        db.EmployeeProfiles.Add(profile);

        await db.SaveChangesAsync(ct);
        await office.ApplyAsync(profile, clock.Today, ct);
        return NoContent();
    }

    [HttpPut("{id:guid}")]
    [Authorize(Policy = HrPolicies.Manage)]
    public async Task<IActionResult> Update(Guid id, UpdateHrEmployeeRequest r, CancellationToken ct)
    {
        var profile = await Find(id, ct);
        var user = await db.Users.SingleAsync(u => u.Id == profile.UserId, ct);
        await EnsureReferencesAsync(r.BranchLocationId, r.DepartmentId, r.SectionId, null, r.ManagerId, ct);

        db.ExpectVersion(profile, r.RowVersion);
        user.UpdateProfile(r.FullName, r.Email, r.Phone, user.EmployeeNumber);
        user.SetPreferredLanguage(r.PreferredLanguage);

        var branchChanged = profile.BranchLocationId != r.BranchLocationId;
        profile.SetPlacement(r.BranchLocationId, r.DepartmentId, r.SectionId, r.ManagerId);
        profile.SetJob(r.JobTitleId, r.GradeId, r.ContractTypeId, r.HireDate);
        profile.SetPersonal(r.Nationality, r.BirthDate, r.EmergencyContactName, r.EmergencyContactPhone, r.Notes);
        profile.SetDocuments(r.IdNumber, r.IdExpiry, r.PassportNumber, r.PassportExpiry, r.ResidencyNumber, r.ResidencyExpiry, r.Iban);

        await db.SaveChangesAsync(ct);
        if (branchChanged) await office.ApplyAsync(profile, clock.Today, ct);
        return NoContent();
    }

    [HttpPost("{id:guid}/schedule")]
    [Authorize(Policy = HrPolicies.Manage)]
    public async Task<IActionResult> AssignSchedule(Guid id, AssignScheduleRequest r, CancellationToken ct)
    {
        var profile = await Find(id, ct);
        if (r.WorkScheduleId is { } scheduleId && !await db.WorkSchedules.AnyAsync(s => s.Id == scheduleId && s.IsActive, ct))
            throw new DomainException("schedule.not_found", "Work schedule not found.");

        profile.SetSchedule(r.WorkScheduleId);
        await db.SaveChangesAsync(ct);
        await office.ApplyAsync(profile, r.EffectiveFrom, ct);
        return NoContent();
    }

    [HttpPost("{id:guid}/salary")]
    [Authorize(Policy = HrPolicies.Manage)]
    public async Task<IActionResult> ChangeSalary(Guid id, ChangeSalaryRequest r, CancellationToken ct)
    {
        var profile = await Find(id, ct);
        db.SalaryChanges.Add(profile.ChangeSalary(r.NewSalary, r.EffectiveFrom, r.Reason, me.RequiredId));
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    /// <summary>
    /// Readable by anyone with an HR view, within the usual scope: a department manager sees
    /// their own people only. Unlike salary, compliance is not restricted to HR managers.
    /// </summary>
    [HttpGet("{id:guid}/compliance")]
    public async Task<Attendance.ComplianceDto> Compliance(Guid id, [FromQuery] int? year, [FromQuery] int? month, CancellationToken ct)
    {
        var profile = await Find(id, ct);
        await scope.EnsureCanSeeEmployeeAsync(profile.UserId, ct);
        return await compliance.ForMonthAsync(profile.UserId, year, month, ct);
    }

    [HttpGet("{id:guid}/salary-history")]
    [Authorize(Policy = HrPolicies.Manage)]
    public async Task<IReadOnlyList<SalaryHistoryRow>> SalaryHistory(Guid id, CancellationToken ct)
    {
        var profile = await Find(id, ct);
        await scope.EnsureCanSeeEmployeeAsync(profile.UserId, ct);
        var history = await db.SalaryChanges.AsNoTracking().Where(s => s.EmployeeId == profile.UserId)
            .OrderByDescending(s => s.EffectiveFrom).ToListAsync(ct);
        var names = await db.Users.AsNoTracking().ToDictionaryAsync(u => u.Id, u => u.FullName, ct);
        return history.Select(s => new SalaryHistoryRow(s.OldSalary, s.NewSalary, s.EffectiveFrom, s.Reason,
            names.GetValueOrDefault(s.DecidedBy), s.CreatedAt)).ToList();
    }

    [HttpPost("{id:guid}/suspend")]
    [Authorize(Policy = HrPolicies.Manage)]
    public async Task<IActionResult> Suspend(Guid id, CancellationToken ct)
    {
        var profile = await Find(id, ct);
        profile.Suspend();
        await db.SaveChangesAsync(ct);
        await office.StopAsync(profile.UserId, clock.Today, ct);
        return NoContent();
    }

    [HttpPost("{id:guid}/reinstate")]
    [Authorize(Policy = HrPolicies.Manage)]
    public async Task<IActionResult> Reinstate(Guid id, CancellationToken ct)
    {
        var profile = await Find(id, ct);
        profile.Reinstate();
        await db.SaveChangesAsync(ct);
        await office.ApplyAsync(profile, clock.Today, ct);
        return NoContent();
    }

    /// <summary>Ends service: the account stops working, the schedule stops, and the history stays.</summary>
    [HttpPost("{id:guid}/end-service")]
    [Authorize(Policy = HrPolicies.Manage)]
    public async Task<IActionResult> EndService(Guid id, EndServiceRequest r, CancellationToken ct)
    {
        var profile = await Find(id, ct);
        if (profile.UserId == me.RequiredId)
            throw new DomainException("user.self_delete", "You cannot end your own service.");

        var user = await db.Users.SingleAsync(u => u.Id == profile.UserId, ct);
        profile.EndService(r.EndDate, r.Reason);
        user.Deactivate();
        await db.SaveChangesAsync(ct);
        await office.StopAsync(profile.UserId, r.EndDate.AddDays(1), ct);
        return NoContent();
    }

    private async Task<EmployeeProfile> Find(Guid id, CancellationToken ct) =>
        await db.EmployeeProfiles.SingleOrDefaultAsync(p => p.Id == id, ct)
        ?? throw new DomainException("employee.not_found", "Employee not found.");

    /// <summary>Field staff sign in to the collector app, so their account is always a collector.</summary>
    private static UserRole ParseRole(string role, Workforce workforce) =>
        workforce == Workforce.Field ? UserRole.Collector
        : Enum.TryParse<UserRole>(role, true, out var parsed) && OfficeRoles.Contains(parsed)
            ? parsed
            : throw new DomainException("user.invalid_role", "This role is not an office role.");

    private async Task EnsureReferencesAsync(Guid branchId, Guid departmentId, Guid? sectionId, Guid? scheduleId,
        Guid? managerId, CancellationToken ct)
    {
        if (!await db.Locations.AnyAsync(l => l.Id == branchId && l.IsActive, ct))
            throw new DomainException("branch.not_found", "Branch not found.");
        if (!await db.Departments.AnyAsync(d => d.Id == departmentId && d.IsActive, ct))
            throw new DomainException("department.not_found", "Department not found.");
        if (sectionId is { } s && !await db.Sections.AnyAsync(x => x.Id == s && x.DepartmentId == departmentId && x.IsActive, ct))
            throw new DomainException("section.not_in_department", "The section does not belong to that department.");
        if (scheduleId is { } sc && !await db.WorkSchedules.AnyAsync(x => x.Id == sc && x.IsActive, ct))
            throw new DomainException("schedule.not_found", "Work schedule not found.");
        if (managerId is { } m && !await db.Users.AnyAsync(u => u.Id == m && u.IsActive, ct))
            throw new DomainException("manager.not_found", "Manager not found.");
    }

    private sealed record Context(
        IReadOnlyDictionary<Guid, User> Users, IReadOnlyDictionary<Guid, string> Departments,
        IReadOnlyDictionary<Guid, string> Sections, IReadOnlyDictionary<Guid, string> JobTitles,
        IReadOnlyDictionary<Guid, string> Branches, IReadOnlyDictionary<Guid, string> Schedules);

    private async Task<Context> LoadContextAsync(CancellationToken ct) => new(
        await db.Users.AsNoTracking().ToDictionaryAsync(u => u.Id, ct),
        await db.Departments.AsNoTracking().ToDictionaryAsync(d => d.Id, d => d.NameAr, ct),
        await db.Sections.AsNoTracking().ToDictionaryAsync(s => s.Id, s => s.NameAr, ct),
        await db.JobTitles.AsNoTracking().ToDictionaryAsync(j => j.Id, j => j.NameAr, ct),
        await db.Locations.AsNoTracking().ToDictionaryAsync(l => l.Id, l => l.NameAr, ct),
        await db.WorkSchedules.AsNoTracking().ToDictionaryAsync(s => s.Id, s => s.NameAr, ct));

    private static HrEmployeeRow ToRow(EmployeeProfile p, Context c)
    {
        var user = c.Users.GetValueOrDefault(p.UserId);
        return new HrEmployeeRow(p.Id, user?.FullName ?? "?", user?.EmployeeNumber ?? "?", user?.Role.ToString() ?? "?",
            user?.Phone, p.DepartmentId, c.Departments.GetValueOrDefault(p.DepartmentId, "?"),
            p.SectionId is { } s ? c.Sections.GetValueOrDefault(s) : null,
            p.JobTitleId is { } j ? c.JobTitles.GetValueOrDefault(j) : null,
            c.Branches.GetValueOrDefault(p.BranchLocationId, "?"),
            p.ManagerId is { } m ? c.Users.GetValueOrDefault(m)?.FullName : null,
            p.HireDate, p.Status.ToString(),
            p.WorkScheduleId is { } w ? c.Schedules.GetValueOrDefault(w) : null,
            p.IsActive, p.Workforce.ToString());
    }
}
