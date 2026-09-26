using System.ComponentModel.DataAnnotations;
using FieldAttendance.Api.Common;
using FieldAttendance.Api.Data;
using FieldAttendance.Domain.Common;
using FieldAttendance.Domain.Entities;
using FieldAttendance.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FieldAttendance.Api.Features.Discipline;

public sealed record DeductionTypeDto(Guid Id, string NameAr, string NameEn, string Unit, decimal Amount, string Trigger,
    int? TriggerThreshold, Guid? AlternativeWarningLevelId, decimal? MonthlyCapPercent, bool IsActive, byte[] RowVersion);

public sealed record SaveDeductionTypeRequest([Required] string NameAr, [Required] string NameEn, [Required] string Unit,
    decimal Amount, [Required] string Trigger, int? TriggerThreshold, Guid? AlternativeWarningLevelId,
    decimal? MonthlyCapPercent, string? Notes, byte[]? RowVersion);

public sealed record WarningLevelDto(Guid Id, string NameAr, string NameEn, int Order, int ValidityDays, string? Template, bool IsActive, byte[] RowVersion);

public sealed record SaveWarningLevelRequest([Required] string NameAr, [Required] string NameEn,
    [Range(1, 20)] int Order, [Range(1, 1095)] int ValidityDays, string? Template, byte[]? RowVersion);

public sealed record ProposalDto(Guid Id, Guid EmployeeId, string EmployeeName, string? EmployeeNumber, Guid DeductionTypeId,
    string TypeName, string Unit, decimal Units, DateOnly OnDate, string Reason, string Status, decimal EstimatedAmount,
    decimal? ApprovedAmount, string? DecidedByName, string? DecisionNote, Guid? AlternativeWarningLevelId);

public sealed record ScanRequest(DateOnly From, DateOnly To, Guid? EmployeeId);

public sealed record ManualProposalRequest(Guid EmployeeId, Guid DeductionTypeId, DateOnly OnDate, decimal Units, [Required] string Reason);

public sealed record ApproveDeductionRequest(decimal? AdjustedUnits, string? Note);

public sealed record ConvertRequest(Guid WarningLevelId, [Required] string Reason);

public sealed record CancelRequest([Required] string Note);

public sealed record WarningDto(Guid Id, Guid EmployeeId, string EmployeeName, Guid WarningLevelId, string LevelName,
    string Reason, DateTimeOffset IssuedAt, string IssuedByName, DateTimeOffset? AcknowledgedAt, string? Objection,
    string? ObjectionResponse, bool InForce);

public sealed record IssueWarningRequest(Guid EmployeeId, Guid WarningLevelId, [Required] string Reason, DateOnly? OnDate);

public sealed record RespondRequest([Required] string Response);

/// <summary>
/// Discipline: the settings behind it, the proposals the system raises, and the warnings
/// that may replace them. Money is only ever moved by an explicit approval recorded here.
/// </summary>
[ApiController]
[Route("api/hr/discipline")]
[Authorize(Policy = HrPolicies.Read)]
public sealed class DisciplineController(AppDbContext db, DeductionService deductions, PayrollLock payrollLock,
    AccessScope scope, ICurrentUser me, IClock clock) : ControllerBase
{
    [HttpGet("types")]
    public async Task<IReadOnlyList<DeductionTypeDto>> Types([FromQuery] bool includeInactive, CancellationToken ct) =>
        (await db.DeductionTypes.AsNoTracking().Where(t => includeInactive || t.IsActive)
            .OrderBy(t => t.SortOrder).ThenBy(t => t.NameAr).ToListAsync(ct))
        .Select(t => new DeductionTypeDto(t.Id, t.NameAr, t.NameEn, t.Unit.ToString(), t.Amount, t.Trigger.ToString(),
            t.TriggerThreshold, t.AlternativeWarningLevelId, t.MonthlyCapPercent, t.IsActive, t.RowVersion)).ToList();

    [HttpPost("types")]
    [Authorize(Policy = HrPolicies.Manage)]
    public async Task<IActionResult> CreateType(SaveDeductionTypeRequest r, CancellationToken ct)
    {
        db.DeductionTypes.Add(new DeductionType(r.NameAr, r.NameEn, ParseUnit(r.Unit), r.Amount, ParseTrigger(r.Trigger),
            r.TriggerThreshold, r.AlternativeWarningLevelId, r.MonthlyCapPercent, r.Notes));
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    [HttpPut("types/{id:guid}")]
    [Authorize(Policy = HrPolicies.Manage)]
    public async Task<IActionResult> UpdateType(Guid id, SaveDeductionTypeRequest r, CancellationToken ct)
    {
        var type = await db.DeductionTypes.SingleOrDefaultAsync(t => t.Id == id, ct)
            ?? throw new DomainException("deduction_type.not_found", "Deduction type not found.");
        db.ExpectVersion(type, r.RowVersion);
        type.Rename(r.NameAr, r.NameEn, r.Notes);
        type.SetRules(ParseUnit(r.Unit), r.Amount, ParseTrigger(r.Trigger), r.TriggerThreshold,
            r.AlternativeWarningLevelId, r.MonthlyCapPercent);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    [HttpDelete("types/{id:guid}")]
    [Authorize(Policy = HrPolicies.Manage)]
    public async Task<IActionResult> DeleteType(Guid id, CancellationToken ct)
    {
        var type = await db.DeductionTypes.SingleOrDefaultAsync(t => t.Id == id, ct)
            ?? throw new DomainException("deduction_type.not_found", "Deduction type not found.");
        type.Deactivate();
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    [HttpGet("warning-levels")]
    public async Task<IReadOnlyList<WarningLevelDto>> Levels(CancellationToken ct) =>
        (await db.WarningLevels.AsNoTracking().Where(l => l.IsActive).OrderBy(l => l.Order).ToListAsync(ct))
        .Select(l => new WarningLevelDto(l.Id, l.NameAr, l.NameEn, l.Order, l.ValidityDays, l.Template, l.IsActive, l.RowVersion)).ToList();

    [HttpPost("warning-levels")]
    [Authorize(Policy = HrPolicies.Manage)]
    public async Task<IActionResult> CreateLevel(SaveWarningLevelRequest r, CancellationToken ct)
    {
        db.WarningLevels.Add(new WarningLevel(r.NameAr, r.NameEn, r.Order, r.ValidityDays, r.Template));
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    [HttpPut("warning-levels/{id:guid}")]
    [Authorize(Policy = HrPolicies.Manage)]
    public async Task<IActionResult> UpdateLevel(Guid id, SaveWarningLevelRequest r, CancellationToken ct)
    {
        var level = await db.WarningLevels.SingleOrDefaultAsync(l => l.Id == id, ct)
            ?? throw new DomainException("warning_level.not_found", "Warning level not found.");
        db.ExpectVersion(level, r.RowVersion);
        level.Rename(r.NameAr, r.NameEn, null);
        level.SetOrder(r.Order);
        level.SetValidity(r.ValidityDays);
        level.SetTemplate(r.Template);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    [HttpDelete("warning-levels/{id:guid}")]
    [Authorize(Policy = HrPolicies.Manage)]
    public async Task<IActionResult> DeleteLevel(Guid id, CancellationToken ct)
    {
        var level = await db.WarningLevels.SingleOrDefaultAsync(l => l.Id == id, ct)
            ?? throw new DomainException("warning_level.not_found", "Warning level not found.");
        level.Deactivate();
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    /// <summary>Raises proposals from the attendance of a period. Safe to run again: nothing duplicates.</summary>
    [HttpPost("scan")]
    [Authorize(Policy = HrPolicies.Manage)]
    public async Task<IActionResult> Scan(ScanRequest r, CancellationToken ct)
    {
        var created = await deductions.ScanAsync(r.From, r.To, r.EmployeeId, ct);
        return Ok(new { created });
    }

    [HttpGet("proposals")]
    public async Task<IReadOnlyList<ProposalDto>> Proposals([FromQuery] string status = "Proposed",
        [FromQuery] DateOnly? from = null, [FromQuery] DateOnly? to = null, CancellationToken ct = default)
    {
        var query = db.DeductionProposals.AsNoTracking().Where(p => p.IsActive);
        if (Enum.TryParse<DeductionStatus>(status, true, out var parsed)) query = query.Where(p => p.Status == parsed);
        if (from is { } f) query = query.Where(p => p.OnDate >= f);
        if (to is { } t) query = query.Where(p => p.OnDate <= t);

        var list = await query.OrderBy(p => p.OnDate).Take(500).ToListAsync(ct);
        var types = await db.DeductionTypes.AsNoTracking().ToDictionaryAsync(t => t.Id, ct);
        var users = await db.Users.AsNoTracking().ToDictionaryAsync(u => u.Id, ct);
        var salaries = await db.EmployeeProfiles.AsNoTracking().ToDictionaryAsync(p => p.UserId, p => p.BasicSalary, ct);
        var policy = await deductions.PolicyAsync(ct);

        return list.Select(p =>
        {
            var type = types.GetValueOrDefault(p.DeductionTypeId);
            var user = users.GetValueOrDefault(p.EmployeeId);
            var amount = type is null ? 0m
                : Domain.Payroll.PayrollMath.DeductionAmount(salaries.GetValueOrDefault(p.EmployeeId), policy, type.Unit, p.Units);
            return new ProposalDto(p.Id, p.EmployeeId, user?.FullName ?? "?", user?.EmployeeNumber, p.DeductionTypeId,
                type?.NameAr ?? "?", type?.Unit.ToString() ?? "Day", p.Units, p.OnDate, p.Reason, p.Status.ToString(),
                amount, p.ApprovedAmount, p.DecidedBy is { } d ? users.GetValueOrDefault(d)?.FullName : null,
                p.DecisionNote, type?.AlternativeWarningLevelId);
        }).ToList();
    }

    [HttpPost("proposals")]
    [Authorize(Policy = HrPolicies.Manage)]
    public async Task<IActionResult> CreateManual(ManualProposalRequest r, CancellationToken ct)
    {
        if (!await db.DeductionTypes.AnyAsync(t => t.Id == r.DeductionTypeId && t.IsActive, ct))
            throw new DomainException("deduction_type.not_found", "Deduction type not found.");
        await payrollLock.EnsureOpenAsync(r.OnDate, ct);
        db.DeductionProposals.Add(new DeductionProposal(r.EmployeeId, r.DeductionTypeId, r.OnDate, r.Units, r.Reason, null, me.RequiredId));
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    /// <summary>Approving freezes the money figure so a later salary change cannot rewrite the past.</summary>
    [HttpPost("proposals/{id:guid}/approve")]
    [Authorize(Policy = HrPolicies.Manage)]
    public async Task<IActionResult> Approve(Guid id, ApproveDeductionRequest r, CancellationToken ct)
    {
        var proposal = await Find(id, ct);
        if (r.AdjustedUnits is { } units && units > 0)
        {
            var preview = new DeductionProposal(proposal.EmployeeId, proposal.DeductionTypeId, proposal.OnDate, units, proposal.Reason, null, null);
            proposal.Approve(me.RequiredId, clock.Now, await deductions.AmountAsync(preview, ct), units, r.Note);
        }
        else
        {
            proposal.Approve(me.RequiredId, clock.Now, await deductions.AmountAsync(proposal, ct), null, r.Note);
        }
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    [HttpPost("proposals/{id:guid}/convert-to-warning")]
    [Authorize(Policy = HrPolicies.Manage)]
    public async Task<IActionResult> Convert(Guid id, ConvertRequest r, CancellationToken ct)
    {
        var proposal = await Find(id, ct);
        if (!await db.WarningLevels.AnyAsync(l => l.Id == r.WarningLevelId && l.IsActive, ct))
            throw new DomainException("warning_level.not_found", "Warning level not found.");

        var warning = new Warning(proposal.EmployeeId, r.WarningLevelId, r.Reason, me.RequiredId, clock.Now, proposal.OnDate);
        db.Warnings.Add(warning);
        proposal.ConvertToWarning(me.RequiredId, clock.Now, warning.Id, r.Reason);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    [HttpPost("proposals/{id:guid}/cancel")]
    [Authorize(Policy = HrPolicies.Manage)]
    public async Task<IActionResult> Cancel(Guid id, CancelRequest r, CancellationToken ct)
    {
        var proposal = await Find(id, ct);
        proposal.Cancel(me.RequiredId, clock.Now, r.Note);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    [HttpGet("warnings")]
    public async Task<IReadOnlyList<WarningDto>> Warnings([FromQuery] Guid? employeeId, CancellationToken ct)
    {
        var query = db.Warnings.AsNoTracking().Where(w => w.IsActive);
        if (employeeId is { } e)
        {
            await scope.EnsureCanSeeEmployeeAsync(e, ct);
            query = query.Where(w => w.EmployeeId == e);
        }

        var list = await query.OrderByDescending(w => w.IssuedAt).Take(500).ToListAsync(ct);
        var levels = await db.WarningLevels.AsNoTracking().ToDictionaryAsync(l => l.Id, ct);
        var users = await db.Users.AsNoTracking().ToDictionaryAsync(u => u.Id, u => u.FullName, ct);
        var today = clock.Today;

        return list.Select(w =>
        {
            var level = levels.GetValueOrDefault(w.WarningLevelId);
            return new WarningDto(w.Id, w.EmployeeId, users.GetValueOrDefault(w.EmployeeId, "?"), w.WarningLevelId,
                level?.NameAr ?? "?", w.Reason, w.IssuedAt, users.GetValueOrDefault(w.IssuedBy, "?"),
                w.AcknowledgedAt, w.Objection, w.ObjectionResponse, w.IsInForce(today, level?.ValidityDays ?? 0));
        }).ToList();
    }

    [HttpPost("warnings")]
    [Authorize(Policy = HrPolicies.Manage)]
    public async Task<IActionResult> Issue(IssueWarningRequest r, CancellationToken ct)
    {
        db.Warnings.Add(new Warning(r.EmployeeId, r.WarningLevelId, r.Reason, me.RequiredId, clock.Now, r.OnDate));
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    [HttpPost("warnings/{id:guid}/respond")]
    [Authorize(Policy = HrPolicies.Manage)]
    public async Task<IActionResult> Respond(Guid id, RespondRequest r, CancellationToken ct)
    {
        var warning = await db.Warnings.SingleOrDefaultAsync(w => w.Id == id, ct)
            ?? throw new DomainException("warning.not_found", "Warning not found.");
        warning.RespondToObjection(r.Response);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    /// <summary>Decisions are refused once the month they belong to has been paid.</summary>
    private async Task<DeductionProposal> Find(Guid id, CancellationToken ct)
    {
        var proposal = await db.DeductionProposals.SingleOrDefaultAsync(p => p.Id == id && p.IsActive, ct)
            ?? throw new DomainException("deduction.not_found", "Proposal not found.");
        await payrollLock.EnsureOpenAsync(proposal.OnDate, ct);
        return proposal;
    }

    private static DeductionUnit ParseUnit(string unit) =>
        Enum.TryParse<DeductionUnit>(unit, true, out var parsed) ? parsed
            : throw new DomainException("deduction_type.unit", "Unknown unit.");

    private static DeductionTrigger ParseTrigger(string trigger) =>
        Enum.TryParse<DeductionTrigger>(trigger, true, out var parsed) ? parsed
            : throw new DomainException("deduction_type.trigger", "Unknown trigger.");
}
