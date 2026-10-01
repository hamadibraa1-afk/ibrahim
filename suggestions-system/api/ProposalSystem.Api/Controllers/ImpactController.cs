using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ProposalSystem.Api.Common;
using ProposalSystem.Api.Data;
using ProposalSystem.Api.Domain;
using ProposalSystem.Api.Realtime;
using ProposalSystem.Api.Security;
using ProposalSystem.Api.Workflow;

namespace ProposalSystem.Api.Controllers;

public sealed record MeasureImpactRequest(
    decimal? ActualAnnualSavings,
    decimal? ActualAnnualRevenue,
    decimal? ImplementationCost,
    decimal? HoursSavedPerMonth,
    int? BeneficiariesReached,
    decimal? SatisfactionBefore,
    decimal? SatisfactionAfter,
    decimal? TargetAchievementPercent,
    int? ImpactRating,
    string? Summary,
    string? EvidenceReference,
    Dictionary<string, string?>? CustomFields);

public sealed record ReviewImpactRequest(bool Approve, string? Notes);

public sealed record DepartmentImpactDto(string Department, int VerifiedCount, decimal TotalAnnualBenefit, decimal TotalImplementationCost, decimal? RoiPercent);

public sealed record TopImpactDto(int ProposalId, string ProposalCode, string Title, string Department, decimal? TotalAnnualBenefit, decimal? RoiPercent, int? ImpactRating);

public sealed record ImpactReportDto(
    int ApprovedProposals,
    int Scheduled,
    int Open,
    int Overdue,
    int AwaitingVerification,
    int Verified,
    decimal TotalAnnualSavings,
    decimal TotalAnnualRevenue,
    decimal TotalImplementationCost,
    decimal TotalAnnualBenefit,
    decimal? PortfolioRoiPercent,
    decimal TotalHoursSavedPerMonth,
    int TotalBeneficiaries,
    decimal? AverageTargetAchievement,
    decimal? AverageImpactRating,
    IReadOnlyList<DepartmentImpactDto> ByDepartment,
    IReadOnlyList<TopImpactDto> TopByRoi);

/// <summary>
/// Post-implementation "Actual Impact Measurement": 3–6 months after approval the implementation
/// owner records what the proposal really delivered; the administrator verifies it, and only
/// verified figures enter the ROI and institutional-impact reports.
/// </summary>
[ApiController]
[Authorize]
public sealed class ImpactController(
    AppDbContext db,
    CurrentUser me,
    ProposalAccess access,
    ProposalMapper mapper,
    AuditTrail audit,
    NotificationService notifications,
    CustomFieldWriter customFields,
    TimeProvider clock) : ControllerBase
{
    private Viewer Viewer => new(me.Id, me.Role);

    /// <summary>Approved proposals in impact tracking: all for admins, the ones they own for everyone else.</summary>
    [HttpGet("api/impact")]
    public async Task<List<ProposalDto>> List([FromQuery] ImpactAssessmentStatus? status, [FromQuery] bool? overdue, CancellationToken ct)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var q = db.Proposals.Where(p => p.Impact != null);
        if (!me.IsAdmin) q = q.Where(p => p.OwnerId == me.Id);
        if (status is { } s) q = q.Where(p => p.Impact!.Status == s);
        if (overdue == true) q = q.Where(p => (p.Impact!.Status == ImpactAssessmentStatus.Open || p.Impact.Status == ImpactAssessmentStatus.Scheduled) && p.Impact.DueAt < now);
        var list = await q.OrderBy(p => p.Impact!.DueAt).Take(500).WithDetails().ToListAsync(ct);
        return list.Select(p => mapper.ToDto(p, Viewer)).ToList();
    }

    [HttpPut("api/proposals/{id:int}/impact")]
    public async Task<ProposalDto> Measure(int id, MeasureImpactRequest req, CancellationToken ct)
    {
        var p = await Load(id, ct);
        if (!access.CanMeasureImpact(p, Viewer))
            throw ApiException.Forbidden("قياس الأثر متاح لمسؤول التنفيذ بعد فتح نافذة القياس (3 أشهر من الاعتماد).");
        await ValidateAsync(req, ct);

        var impact = p.Impact!;
        impact.ActualAnnualSavings = req.ActualAnnualSavings;
        impact.ActualAnnualRevenue = req.ActualAnnualRevenue;
        impact.ImplementationCost = req.ImplementationCost;
        impact.HoursSavedPerMonth = req.HoursSavedPerMonth;
        impact.BeneficiariesReached = req.BeneficiariesReached;
        impact.SatisfactionBefore = req.SatisfactionBefore;
        impact.SatisfactionAfter = req.SatisfactionAfter;
        impact.TargetAchievementPercent = req.TargetAchievementPercent;
        impact.ImpactRating = req.ImpactRating;
        impact.Summary = req.Summary?.Trim();
        impact.EvidenceReference = string.IsNullOrWhiteSpace(req.EvidenceReference) ? null : req.EvidenceReference.Trim();
        await customFields.ApplyAsync(p, req.CustomFields, [FormSections.ImpactMeasurement], true, ct);

        var actor = await db.Users.FirstAsync(u => u.Id == me.Id, ct);
        var now = clock.GetUtcNow().UtcDateTime;
        impact.Status = ImpactAssessmentStatus.Submitted;
        impact.MeasuredAt = now;
        impact.MeasuredById = actor.Id;
        impact.MeasuredBy = actor;
        p.UpdatedAt = now;

        var roi = impact.RoiPercent is { } r ? $" — العائد على الاستثمار {r}%" : "";
        audit.Record(p, actor, AuditActions.ImpactMeasured, notes: $"صافي المنفعة السنوية: {impact.TotalAnnualBenefit?.ToString("N0", System.Globalization.CultureInfo.InvariantCulture) ?? "—"}{roi}");
        var admins = await notifications.UsersInRolesAsync(ct, UserRole.Admin);
        notifications.Notify(admins.Where(a => a != me.Id), NotificationTypes.ImpactSubmitted,
            $"سُجّل قياس الأثر الفعلي للمقترح {p.ProposalCode} «{p.Title}» وهو بانتظار الاعتماد.", p);
        return await CommitAsync(p, ct);
    }

    [HttpPost("api/proposals/{id:int}/impact/review")]
    public async Task<ProposalDto> Review(int id, ReviewImpactRequest req, CancellationToken ct)
    {
        var p = await Load(id, ct);
        if (!access.CanVerifyImpact(p, Viewer))
            throw ApiException.Forbidden();
        var actor = await db.Users.FirstAsync(u => u.Id == me.Id, ct);
        var now = clock.GetUtcNow().UtcDateTime;
        var impact = p.Impact!;
        var notes = req.Notes?.Trim();

        if (req.Approve)
        {
            impact.Status = ImpactAssessmentStatus.Verified;
            impact.VerifiedAt = now;
            impact.VerifiedById = actor.Id;
            impact.VerifiedBy = actor;
            impact.ReviewNotes = string.IsNullOrEmpty(notes) ? null : notes;
            audit.Record(p, actor, AuditActions.ImpactVerified, notes: notes);
            var recipients = new[] { p.SubmitterId, p.OwnerId ?? p.SubmitterId };
            notifications.Notify(recipients, NotificationTypes.ImpactVerified,
                $"اعتُمد قياس الأثر الفعلي للمقترح {p.ProposalCode} «{p.Title}» وأُدرج في تقارير العائد المؤسسي.", p);
        }
        else
        {
            if (string.IsNullOrEmpty(notes))
                throw ApiException.BadRequest("الرجاء توضيح ما يلزم تصحيحه في القياس.");
            impact.Status = ImpactAssessmentStatus.Open;
            impact.ReviewNotes = notes;
            audit.Record(p, actor, AuditActions.ImpactReturned, notes: notes);
            if (impact.MeasuredById is { } measurer)
                notifications.Notify(measurer, NotificationTypes.ImpactMeasurementDue,
                    $"أُعيد قياس أثر المقترح {p.ProposalCode} للتصحيح: {notes}", p);
        }
        p.UpdatedAt = now;
        return await CommitAsync(p, ct);
    }

    /// <summary>ROI and institutional impact, from verified measurements only.</summary>
    [HttpGet("api/impact/report")]
    [Authorize(Roles = nameof(UserRole.Admin))]
    public async Task<ImpactReportDto> Report(CancellationToken ct)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        // Aggregated in memory: SQLite cannot sum decimals, and the volume is a few hundred rows.
        var all = await db.ImpactAssessments.Include(i => i.Proposal).AsNoTracking().ToListAsync(ct);
        var verified = all.Where(i => i.Status == ImpactAssessmentStatus.Verified).ToList();

        decimal Sum(Func<ImpactAssessment, decimal?> f) => verified.Sum(i => f(i) ?? 0);
        var cost = Sum(i => i.ImplementationCost);
        var benefit = Sum(i => i.TotalAnnualBenefit);

        var byDept = verified.GroupBy(i => i.Proposal.Department)
            .Select(g =>
            {
                var b = g.Sum(i => i.TotalAnnualBenefit ?? 0);
                var c = g.Sum(i => i.ImplementationCost ?? 0);
                return new DepartmentImpactDto(g.Key, g.Count(), b, c, c > 0 ? Math.Round((b - c) / c * 100m, 1) : null);
            })
            .OrderByDescending(d => d.TotalAnnualBenefit)
            .ToList();

        var top = verified
            .OrderByDescending(i => i.RoiPercent ?? decimal.MinValue).ThenByDescending(i => i.TotalAnnualBenefit ?? 0)
            .Take(5)
            .Select(i => new TopImpactDto(i.ProposalId, i.Proposal.ProposalCode, i.Proposal.Title, i.Proposal.Department, i.TotalAnnualBenefit, i.RoiPercent, i.ImpactRating))
            .ToList();

        static decimal? Avg(IEnumerable<decimal> values)
        {
            var list = values.ToList();
            return list.Count > 0 ? Math.Round(list.Average(), 1) : null;
        }

        return new ImpactReportDto(
            all.Count,
            all.Count(i => i.Status == ImpactAssessmentStatus.Scheduled),
            all.Count(i => i.Status == ImpactAssessmentStatus.Open),
            all.Count(i => i.Status is ImpactAssessmentStatus.Scheduled or ImpactAssessmentStatus.Open && i.DueAt < now),
            all.Count(i => i.Status == ImpactAssessmentStatus.Submitted),
            verified.Count,
            Sum(i => i.ActualAnnualSavings),
            Sum(i => i.ActualAnnualRevenue),
            cost,
            benefit,
            cost > 0 ? Math.Round((benefit - cost) / cost * 100m, 1) : null,
            Sum(i => i.HoursSavedPerMonth),
            verified.Sum(i => i.BeneficiariesReached ?? 0),
            Avg(verified.Where(i => i.TargetAchievementPercent is not null).Select(i => i.TargetAchievementPercent!.Value)),
            Avg(verified.Where(i => i.ImpactRating is not null).Select(i => (decimal)i.ImpactRating!.Value)),
            byDept,
            top);
    }

    private async Task ValidateAsync(MeasureImpactRequest req, CancellationToken ct)
    {
        static void NonNegative(decimal? v, string label)
        {
            if (v is < 0) throw ApiException.BadRequest($"{label} لا يمكن أن تكون سالبة.");
        }
        static void Percent(decimal? v, string label, decimal max = 100)
        {
            if (v is < 0 || v > max) throw ApiException.BadRequest($"{label} يجب أن تكون بين 0 و{max}.");
        }

        NonNegative(req.ActualAnnualSavings, "الوفورات السنوية");
        NonNegative(req.ActualAnnualRevenue, "الإيرادات/التبرعات الإضافية");
        NonNegative(req.ImplementationCost, "تكلفة التطبيق");
        NonNegative(req.HoursSavedPerMonth, "الساعات الموفّرة");
        if (req.BeneficiariesReached is < 0) throw ApiException.BadRequest("عدد المستفيدين لا يمكن أن يكون سالباً.");
        Percent(req.SatisfactionBefore, "نسبة الرضا قبل التطبيق");
        Percent(req.SatisfactionAfter, "نسبة الرضا بعد التطبيق");
        Percent(req.TargetAchievementPercent, "نسبة تحقق المستهدف", 200);
        if (req.ImpactRating is < 1 or > 5) throw ApiException.BadRequest("تقييم الأثر يجب أن يكون من 1 إلى 5.");
        if (req.Summary is { Length: > 4000 }) throw ApiException.BadRequest("الملخص طويل جداً.");

        // Required flags for the dedicated fields come from the form settings (section ImpactMeasurement).
        var required = await db.FormFields
            .Where(f => f.IsSystem && f.IsActive && f.IsRequired && f.Section == FormSections.ImpactMeasurement)
            .Select(f => new { f.FieldKey, f.LabelAr })
            .ToListAsync(ct);
        var provided = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase)
        {
            ["actualAnnualSavings"] = req.ActualAnnualSavings is not null,
            ["actualAnnualRevenue"] = req.ActualAnnualRevenue is not null,
            ["implementationCost"] = req.ImplementationCost is not null,
            ["hoursSavedPerMonth"] = req.HoursSavedPerMonth is not null,
            ["beneficiariesReached"] = req.BeneficiariesReached is not null,
            ["satisfactionBefore"] = req.SatisfactionBefore is not null,
            ["satisfactionAfter"] = req.SatisfactionAfter is not null,
            ["targetAchievementPercent"] = req.TargetAchievementPercent is not null,
            ["impactRating"] = req.ImpactRating is not null,
            ["impactSummary"] = !string.IsNullOrWhiteSpace(req.Summary),
            ["evidenceReference"] = !string.IsNullOrWhiteSpace(req.EvidenceReference),
        };
        foreach (var f in required)
        {
            if (provided.TryGetValue(f.FieldKey, out var has) && !has)
                throw ApiException.BadRequest($"الحقل \"{f.LabelAr}\" إلزامي.");
        }
    }

    private async Task<Proposal> Load(int id, CancellationToken ct)
    {
        var p = await db.Proposals.WithDetails().FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw ApiException.NotFound("المقترح غير موجود.");
        if (!access.CanView(p, Viewer))
            throw ApiException.Forbidden();
        if (p.Impact is null)
            throw ApiException.BadRequest("قياس الأثر متاح للمقترحات المعتمدة فقط.");
        return p;
    }

    private async Task<ProposalDto> CommitAsync(Proposal p, CancellationToken ct)
    {
        notifications.ProposalChanged(p);
        await db.SaveChangesAsync(ct);
        await notifications.DispatchAsync(ct);
        return mapper.ToDto(p, Viewer);
    }
}
