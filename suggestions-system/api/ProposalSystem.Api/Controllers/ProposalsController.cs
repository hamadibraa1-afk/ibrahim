using System.Globalization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ProposalSystem.Api.Common;
using ProposalSystem.Api.Data;
using ProposalSystem.Api.Documents;
using ProposalSystem.Api.Domain;
using ProposalSystem.Api.Realtime;
using ProposalSystem.Api.Security;
using ProposalSystem.Api.Workflow;

namespace ProposalSystem.Api.Controllers;

public sealed record CreateProposalRequest(string Title, string ImplementationMechanism, string SubmissionReasons, Dictionary<string, string?>? CustomFields);
public sealed record ScreenDecisionRequest(string Action, string? Notes);
public sealed record CommitteeStudyRequest(ProposalClassification? Classification, NotApplicableReason? NotApplicableReasonValue, string CommitteeStudy, string CommitteeRecommendation);
public sealed record CommitteeSignRequest(int MemberId);
public sealed record ExecutiveDecisionRequest(ExecutiveDecisionValue Decision, string? Notes);
public sealed record AssignOwnerRequest(int OwnerId);
public sealed record EscalateRequest(string Note);
public sealed record BlindReviewPolicyDto(RevealStage Stage, ProposalStatus RevealOnStatus, string Description);

public sealed class ProposalQuery
{
    public bool? Mine { get; set; }
    public bool? AssignedToMe { get; set; }
    public bool? ForScreening { get; set; }
    public bool? ForCommittee { get; set; }
    public bool? ForExecutiveDecision { get; set; }
    public bool? Decided { get; set; }
    public ProposalStatus? Status { get; set; }
    public string? Search { get; set; }
    public string? Department { get; set; }
    public ProposalClassification? Classification { get; set; }
    public bool? Overdue { get; set; }
    public bool? Escalated { get; set; }
    public DateOnly? From { get; set; }
    public DateOnly? To { get; set; }
    public string? Sort { get; set; }
}

[ApiController]
[Route("api/proposals")]
[Authorize]
public sealed class ProposalsController(
    AppDbContext db,
    CurrentUser me,
    ProposalAccess access,
    ProposalMapper mapper,
    IdentityRevealPolicy reveal,
    AuditTrail audit,
    NotificationService notifications,
    CustomFieldWriter customFields,
    IOptions<ImpactTrackingOptions> impactOptions,
    TimeProvider clock) : ControllerBase
{
    private Viewer Viewer => new(me.Id, me.Role);
    private DateTime Now => clock.GetUtcNow().UtcDateTime;

    [HttpGet("blind-review-policy")]
    public BlindReviewPolicyDto BlindReviewPolicy() => new(reveal.Stage, reveal.RevealOnStatus, reveal.Describe());

    [HttpGet]
    public async Task<List<ProposalDto>> GetAll([FromQuery] ProposalQuery q, CancellationToken ct)
    {
        var viewer = Viewer;
        var now = Now;
        var query = db.Proposals.AsQueryable();

        if (!ProposalAccess.IsReviewer(viewer.Role))
            query = query.Where(p => p.SubmitterId == viewer.Id || p.OwnerId == viewer.Id || p.EscalatedToId == viewer.Id);

        if (q.Mine == true) query = query.Where(p => p.SubmitterId == viewer.Id);
        if (q.AssignedToMe == true) query = query.Where(p => p.SubmitterId != viewer.Id && (p.OwnerId == viewer.Id || p.EscalatedToId == viewer.Id));
        if (q.ForScreening == true) query = query.Where(p => ProposalAccess.ScreeningStatuses.Contains(p.Status) && p.SubmitterId != viewer.Id);
        if (q.ForCommittee == true) query = query.Where(p => p.Status == ProposalStatus.WithCommittee && p.SubmitterId != viewer.Id);
        if (q.ForExecutiveDecision == true) query = query.Where(p => p.Status == ProposalStatus.PendingExecutiveDecision && p.SubmitterId != viewer.Id);
        if (q.Decided == true) query = query.Where(p => ProposalAccess.FinalStatuses.Contains(p.Status));
        if (q.Status is { } status) query = query.Where(p => p.Status == status);
        if (q.Classification is { } cls) query = query.Where(p => p.Classification == cls);
        if (q.Overdue == true) query = query.Where(p => ProposalAccess.SlaTrackedStatuses.Contains(p.Status) && p.SlaDueAt < now);
        if (q.Escalated == true) query = query.Where(p => p.EscalatedAt != null && ProposalAccess.SlaTrackedStatuses.Contains(p.Status));
        if (q.From is { } from) { var f = from.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc); query = query.Where(p => p.SubmittedAt >= f); }
        if (q.To is { } to) { var t = to.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc); query = query.Where(p => p.SubmittedAt < t); }

        if (!string.IsNullOrWhiteSpace(q.Department))
        {
            var dept = q.Department.Trim();
            query = reveal.DepartmentMaskedFor(viewer)
                // Department is masked before the reveal: only filter what this viewer can already see.
                ? query.Where(p => p.Department == dept && (p.IdentityRevealedAt != null || p.SubmitterId == viewer.Id))
                : query.Where(p => p.Department == dept);
        }

        if (!string.IsNullOrWhiteSpace(q.Search))
        {
            var term = q.Search.Trim();
            // Matching on the proposer's name would leak identities to blind reviewers, so they
            // can only search names on proposals already revealed (or their own).
            var anyName = reveal.MaySearchByName(viewer);
            query = query.Where(p => p.Title.Contains(term) || p.ProposalCode.Contains(term)
                                     || (p.Submitter.ArabicName.Contains(term) && (anyName || p.IdentityRevealedAt != null || p.SubmitterId == viewer.Id)));
        }

        query = q.Sort switch
        {
            "oldest" => query.OrderBy(p => p.SubmittedAt),
            "dueSoon" => query.OrderBy(p => p.SlaDueAt),
            "title" => query.OrderBy(p => p.Title),
            _ => query.OrderByDescending(p => p.SubmittedAt),
        };

        var list = await query.Take(500).WithDetails().ToListAsync(ct);
        return list.Select(p => mapper.ToDto(p, viewer)).ToList();
    }

    [HttpGet("{id:int}")]
    public async Task<ProposalDto> Get(int id, CancellationToken ct) => mapper.ToDto(await LoadVisible(id, ct), Viewer);

    [HttpPost]
    public async Task<ProposalDto> Create(CreateProposalRequest req, CancellationToken ct)
    {
        ValidateContent(req);
        var submitter = await db.Users.FirstAsync(u => u.Id == me.Id, ct);
        var now = Now;
        var p = new Proposal
        {
            ProposalCode = await NextCodeAsync(now, ct),
            Title = req.Title.Trim(),
            ImplementationMechanism = req.ImplementationMechanism.Trim(),
            SubmissionReasons = req.SubmissionReasons.Trim(),
            Department = submitter.Department,
            SubmitterId = submitter.Id,
            Submitter = submitter,
            SubmittedAt = now,
        };
        access.EnterStage(p, ProposalStatus.Submitted, now);
        await customFields.ApplyAsync(p, req.CustomFields, [FormSections.SuggestionData, FormSections.SuggestionDetails, FormSections.Impact], true, ct);
        db.Proposals.Add(p);
        await db.SaveChangesAsync(ct);

        audit.Record(p, submitter, AuditActions.Created, null, ProposalStatus.Submitted);
        await NotifyScreenersAsync(p, $"مقترح جديد {p.ProposalCode} «{p.Title}» بانتظار الفرز الأولي.", ct);
        return await CommitAsync(p, ct);
    }

    [HttpPut("{id:int}")]
    public async Task<ProposalDto> Update(int id, CreateProposalRequest req, CancellationToken ct)
    {
        var p = await LoadVisible(id, ct);
        if (!access.CanEdit(p, Viewer))
            throw ApiException.Forbidden("لا يمكن تعديل المقترح في حالته الحالية.");
        ValidateContent(req);

        p.Title = req.Title.Trim();
        p.ImplementationMechanism = req.ImplementationMechanism.Trim();
        p.SubmissionReasons = req.SubmissionReasons.Trim();
        await customFields.ApplyAsync(p, req.CustomFields, [FormSections.SuggestionData, FormSections.SuggestionDetails, FormSections.Impact], true, ct);
        var now = Now;
        p.UpdatedAt = now;

        if (p.Status == ProposalStatus.ReturnedForEdit)
        {
            p.ResubmitCount++;
            p.LastResubmittedAt = now;
            access.EnterStage(p, ProposalStatus.UnderScreening, now);
            audit.Record(p, p.Submitter, AuditActions.Resubmitted, ProposalStatus.ReturnedForEdit, ProposalStatus.UnderScreening);
            await NotifyScreenersAsync(p, $"أُعيد إرسال المقترح {p.ProposalCode} «{p.Title}» بعد التعديل وهو بانتظار الفرز.", ct);
        }
        else
        {
            audit.Record(p, p.Submitter, AuditActions.Edited);
        }
        return await CommitAsync(p, ct);
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        var p = await LoadVisible(id, ct);
        if (!access.CanDelete(p, Viewer))
            throw ApiException.Forbidden("يمكن حذف المقترح فقط قبل بدء الفرز.");
        audit.Record(p, p.Submitter, AuditActions.Deleted, p.Status, null, p.Title);
        db.Proposals.Remove(p);
        await db.SaveChangesAsync(ct);
        AttachmentsController.DeleteFiles(p.Attachments, HttpContext.RequestServices);
        notifications.ProposalChanged(p);
        await notifications.DispatchAsync(ct);
        return NoContent();
    }

    [HttpPost("{id:int}/screen")]
    public async Task<ProposalDto> Screen(int id, ScreenDecisionRequest req, CancellationToken ct)
    {
        var p = await LoadVisible(id, ct);
        if (!access.CanScreen(p, Viewer))
            throw ApiException.Forbidden();
        var actor = await Actor(ct);
        var notes = req.Notes?.Trim();
        var from = p.Status;
        var now = Now;

        switch (req.Action)
        {
            case "Approve":
                p.ScreenerNotes = string.IsNullOrEmpty(notes) ? null : notes;
                access.EnterStage(p, ProposalStatus.WithCommittee, now);
                await CreateCommitteeVotesAsync(p, ct);
                audit.Record(p, actor, AuditActions.ScreenApproved, from, p.Status, notes);
                RevealIfDue(p, now);
                notifications.Notify(p.SubmitterId, NotificationTypes.ScreeningDecision,
                    $"اجتاز مقترحك {p.ProposalCode} «{p.Title}» الفرز الأولي وأُحيل إلى لجنة الدراسة.", p);
                notifications.Notify(p.Votes.Select(v => v.MemberId), NotificationTypes.AssignedToCommittee,
                    $"أُحيل المقترح {p.ProposalCode} «{p.Title}» إلى اللجنة للدراسة والتوصية.", p);
                break;

            case "ReturnForEdit":
                if (string.IsNullOrEmpty(notes))
                    throw ApiException.BadRequest("الرجاء إضافة ملاحظات للموظف.");
                p.ScreenerNotes = notes;
                access.EnterStage(p, ProposalStatus.ReturnedForEdit, now);
                audit.Record(p, actor, AuditActions.ScreenReturned, from, p.Status, notes);
                notifications.Notify(p.SubmitterId, NotificationTypes.ScreeningDecision,
                    $"أُعيد مقترحك {p.ProposalCode} «{p.Title}» إليك للتعديل. اطّلع على ملاحظات الفرز.", p);
                break;

            case "Reject":
                if (string.IsNullOrEmpty(notes))
                    throw ApiException.BadRequest("الرجاء ذكر سبب الرفض.");
                p.ScreenerNotes = notes;
                p.RejectionReason = notes;
                access.EnterStage(p, ProposalStatus.Rejected, now);
                audit.Record(p, actor, AuditActions.ScreenRejected, from, p.Status, notes);
                notifications.Notify(p.SubmitterId, NotificationTypes.ScreeningDecision,
                    $"لم يجتز مقترحك {p.ProposalCode} «{p.Title}» الفرز الأولي. نشكر مبادرتك.", p);
                break;

            default:
                throw ApiException.BadRequest("إجراء فرز غير معروف.");
        }
        return await CommitAsync(p, ct);
    }

    [HttpPost("{id:int}/committee-study")]
    public async Task<ProposalDto> SaveCommitteeStudy(int id, CommitteeStudyRequest req, CancellationToken ct)
    {
        var p = await LoadVisible(id, ct);
        if (!access.CanStudy(p, Viewer))
            throw ApiException.Forbidden();
        if (req.Classification is null)
            throw ApiException.BadRequest("الرجاء تحديد تصنيف المقترح.");
        if (req.Classification == ProposalClassification.NotApplicable && req.NotApplicableReasonValue is null)
            throw ApiException.BadRequest("الرجاء تحديد سبب عدم قابلية التطبيق.");
        if (string.IsNullOrWhiteSpace(req.CommitteeStudy) || string.IsNullOrWhiteSpace(req.CommitteeRecommendation))
            throw ApiException.BadRequest("الرجاء تعبئة الدراسة والتوصية.");

        var study = req.CommitteeStudy.Trim();
        var recommendation = req.CommitteeRecommendation.Trim();
        var reason = req.Classification == ProposalClassification.NotApplicable ? req.NotApplicableReasonValue : null;
        var changed = p.Classification != req.Classification || p.NotApplicableReasonValue != reason
                      || p.CommitteeStudy != study || p.CommitteeRecommendation != recommendation;

        p.Classification = req.Classification;
        p.NotApplicableReasonValue = reason;
        p.CommitteeStudy = study;
        p.CommitteeRecommendation = recommendation;
        p.UpdatedAt = Now;

        // A signature endorses a specific text: if the text changes, earlier signatures no longer apply.
        var resetSignatures = changed && p.Votes.Any(v => v.Signed);
        if (resetSignatures)
        {
            foreach (var v in p.Votes) { v.Signed = false; v.SignedAt = null; }
        }
        audit.Record(p, await Actor(ct), AuditActions.CommitteeStudySaved, notes: resetSignatures ? "تغيّرت الدراسة/التوصية فأُلغيت التوقيعات السابقة." : null);
        return await CommitAsync(p, ct);
    }

    [HttpPost("{id:int}/committee-sign")]
    public async Task<ProposalDto> CommitteeSign(int id, CommitteeSignRequest req, CancellationToken ct)
    {
        var p = await LoadVisible(id, ct);
        // Members sign only for themselves, whatever id the client sends.
        if (req.MemberId != me.Id || !access.CanSign(p, Viewer))
            throw ApiException.Forbidden("لا يمكنك التوقيع على هذا المقترح.");
        var actor = await Actor(ct);
        var now = Now;
        var vote = p.Votes.First(v => v.MemberId == me.Id);
        vote.Signed = true;
        vote.SignedAt = now;
        p.UpdatedAt = now;
        audit.Record(p, actor, AuditActions.CommitteeSigned);

        var admins = await notifications.UsersInRolesAsync(ct, UserRole.Admin);
        if (p.Votes.All(v => v.Signed))
        {
            access.EnterStage(p, ProposalStatus.PendingExecutiveDecision, now);
            RevealIfDue(p, now);
            notifications.Notify(admins.Where(a => a != p.SubmitterId), NotificationTypes.CommitteeSigned,
                $"اكتملت توقيعات اللجنة على المقترح {p.ProposalCode} «{p.Title}» وهو بانتظار القرار التنفيذي.", p);
        }
        else
        {
            notifications.Notify(admins.Where(a => a != p.SubmitterId && a != me.Id), NotificationTypes.CommitteeSigned,
                $"وقّع {actor.ArabicName} على توصية المقترح {p.ProposalCode} ({p.Votes.Count(v => v.Signed)}/{p.Votes.Count}).", p);
        }
        return await CommitAsync(p, ct);
    }

    [HttpPost("{id:int}/executive-decision")]
    public async Task<ProposalDto> ExecutiveDecision(int id, ExecutiveDecisionRequest req, CancellationToken ct)
    {
        var p = await LoadVisible(id, ct);
        if (!access.CanDecide(p, Viewer))
            throw ApiException.Forbidden();
        var notes = req.Notes?.Trim();
        if (req.Decision == ExecutiveDecisionValue.Rejected && string.IsNullOrEmpty(notes))
            throw ApiException.BadRequest("الرجاء ذكر سبب عدم الموافقة.");

        var actor = await Actor(ct);
        var now = Now;
        var from = p.Status;
        p.ExecutiveDecision = req.Decision;
        p.ExecutiveDecisionNotes = string.IsNullOrEmpty(notes) ? null : notes;
        p.ExecutiveDecisionById = actor.Id;
        p.ExecutiveDecisionBy = actor;
        p.ExecutiveDecisionAt = now;

        if (req.Decision == ExecutiveDecisionValue.Accepted)
        {
            access.EnterStage(p, ProposalStatus.Accepted, now);
            audit.Record(p, actor, AuditActions.ExecutiveAccepted, from, p.Status, notes);
            RevealIfDue(p, now);
            ScheduleImpactMeasurement(p, now);
            notifications.Notify(p.SubmitterId, NotificationTypes.ExecutiveDecision,
                $"تهانينا! اعتمدت الإدارة التنفيذية مقترحك {p.ProposalCode} «{p.Title}».", p);
        }
        else
        {
            p.RejectionReason = notes;
            access.EnterStage(p, ProposalStatus.Rejected, now);
            audit.Record(p, actor, AuditActions.ExecutiveRejected, from, p.Status, notes);
            notifications.Notify(p.SubmitterId, NotificationTypes.ExecutiveDecision,
                $"لم تعتمد الإدارة التنفيذية مقترحك {p.ProposalCode} «{p.Title}». اطّلع على الملاحظات.", p);
        }
        return await CommitAsync(p, ct);
    }

    [HttpPost("{id:int}/assign-owner")]
    public async Task<ProposalDto> AssignOwner(int id, AssignOwnerRequest req, CancellationToken ct)
    {
        var p = await LoadVisible(id, ct);
        if (!access.CanAssignOwner(p, Viewer))
            throw ApiException.Forbidden();
        var owner = await db.Users.FirstOrDefaultAsync(u => u.Id == req.OwnerId && u.Status == UserStatus.Active, ct)
                    ?? throw ApiException.BadRequest("الموظف المحدد غير موجود أو موقوف.");
        p.OwnerId = owner.Id;
        p.Owner = owner;
        p.UpdatedAt = Now;
        audit.Record(p, await Actor(ct), AuditActions.OwnerAssigned, notes: owner.ArabicName);
        var impactNote = p.Impact is { } impact ? $" ستكون مسؤولاً عن قياس الأثر الفعلي بين {impact.OpensAt:yyyy-MM-dd} و{impact.DueAt:yyyy-MM-dd}." : "";
        notifications.Notify(owner.Id, NotificationTypes.OwnerAssigned,
            $"عُيّنت مسؤولاً عن متابعة وتنفيذ المقترح {p.ProposalCode} «{p.Title}».{impactNote}", p);
        return await CommitAsync(p, ct);
    }

    /// <summary>Manual escalation by a screener or admin, alongside the automatic one.</summary>
    [HttpPost("{id:int}/escalate")]
    public async Task<ProposalDto> Escalate(int id, EscalateRequest req, CancellationToken ct)
    {
        var p = await LoadVisible(id, ct);
        if (!access.CanEscalate(p, Viewer))
            throw ApiException.Forbidden("يمكن التصعيد فقط للمقترحات المتأخرة عن مهلتها.");
        if (string.IsNullOrWhiteSpace(req.Note))
            throw ApiException.BadRequest("الرجاء كتابة سبب التصعيد.");
        var actor = await Actor(ct);
        p.EscalatedAt = Now;
        p.EscalationNote = req.Note.Trim();
        audit.Record(p, actor, AuditActions.Escalated, notes: p.EscalationNote);
        var admins = await notifications.UsersInRolesAsync(ct, UserRole.Admin);
        notifications.Notify(admins.Where(a => a != me.Id && a != p.SubmitterId), NotificationTypes.Escalation,
            $"صعّد {actor.ArabicName} المقترح {p.ProposalCode} «{p.Title}»: {p.EscalationNote}", p);
        return await CommitAsync(p, ct);
    }

    [HttpGet("{id:int}/form-pdf")]
    public async Task<ContentResult> OfficialForm(int id, CancellationToken ct)
    {
        var p = await LoadVisible(id, ct);
        var fields = await db.FormFields.Where(f => f.IsActive && f.Section == FormSections.Impact).OrderBy(f => f.SortOrder).ToListAsync(ct);
        return Html(DocumentRenderer.OfficialForm(p, fields, reveal.CanSeeIdentity(p, Viewer), reveal.CanSeeDepartment(p, Viewer)));
    }

    [HttpGet("{id:int}/minutes")]
    public async Task<ContentResult> Minutes(int id, CancellationToken ct)
    {
        var p = await LoadVisible(id, ct);
        if (string.IsNullOrEmpty(p.CommitteeRecommendation))
            throw ApiException.BadRequest("لم تُحفظ توصية اللجنة بعد.");
        return Html(DocumentRenderer.Minutes(p));
    }

    [HttpGet("{id:int}/certificate")]
    public async Task<ContentResult> Certificate(int id, CancellationToken ct)
    {
        var p = await LoadVisible(id, ct);
        if (p.Status != ProposalStatus.Accepted)
            throw ApiException.BadRequest("شهادة الشكر تصدر للمقترحات المعتمدة فقط.");
        var name = reveal.CanSeeIdentity(p, Viewer) ? p.Submitter.ArabicName : IdentityRevealPolicy.MaskedName;
        return Html(DocumentRenderer.Certificate(p, name));
    }

    // ---------------------------------------------------------------- helpers

    private async Task<Proposal> LoadVisible(int id, CancellationToken ct)
    {
        var p = await db.Proposals.WithDetails().FirstOrDefaultAsync(x => x.Id == id, ct)
                ?? throw ApiException.NotFound("المقترح غير موجود.");
        if (!access.CanView(p, Viewer))
            throw ApiException.Forbidden("ليست لديك صلاحية لعرض هذا المقترح.");
        return p;
    }

    private async Task<User> Actor(CancellationToken ct) => await db.Users.FirstAsync(u => u.Id == me.Id, ct);

    private async Task<ProposalDto> CommitAsync(Proposal p, CancellationToken ct)
    {
        notifications.ProposalChanged(p);
        await db.SaveChangesAsync(ct);
        await notifications.DispatchAsync(ct);
        return mapper.ToDto(p, Viewer);
    }

    private void RevealIfDue(Proposal p, DateTime now)
    {
        if (reveal.ApplyOnTransition(p, p.Status, now))
            audit.Record(p, null, AuditActions.IdentityRevealed, notes: reveal.Describe());
    }

    private void ScheduleImpactMeasurement(Proposal p, DateTime now)
    {
        if (p.Impact is not null)
            return;
        var o = impactOptions.Value;
        p.Impact = new ImpactAssessment
        {
            Proposal = p,
            Status = ImpactAssessmentStatus.Scheduled,
            OpensAt = now.AddMonths(o.WindowOpensAfterMonths),
            DueAt = now.AddMonths(o.DueAfterMonths),
        };
        audit.Record(p, null, AuditActions.ImpactScheduled,
            notes: $"قياس الأثر الفعلي بين {p.Impact.OpensAt:yyyy-MM-dd} و{p.Impact.DueAt:yyyy-MM-dd}.");
    }

    private async Task CreateCommitteeVotesAsync(Proposal p, CancellationToken ct)
    {
        var members = await db.Users
            .Where(u => u.Role == UserRole.CommitteeMember && u.Status == UserStatus.Active && u.Id != p.SubmitterId)
            .ToListAsync(ct);
        if (members.Count == 0)
            throw ApiException.Conflict("لا يوجد أعضاء لجنة نشطون. أضف أعضاء اللجنة من إدارة المستخدمين أولاً.");
        db.CommitteeVotes.RemoveRange(p.Votes);
        p.Votes.Clear();
        foreach (var m in members)
            p.Votes.Add(new CommitteeVote { MemberId = m.Id, Member = m });
    }

    private async Task NotifyScreenersAsync(Proposal p, string message, CancellationToken ct)
    {
        var recipients = await notifications.UsersInRolesAsync(ct, UserRole.Screener, UserRole.Admin);
        notifications.Notify(recipients.Where(r => r != p.SubmitterId), NotificationTypes.NewSubmission, message, p);
    }

    private async Task<string> NextCodeAsync(DateTime now, CancellationToken ct)
    {
        var prefix = string.Create(CultureInfo.InvariantCulture, $"SCI-{now.Year}-");
        var last = await db.Proposals.Where(p => p.ProposalCode.StartsWith(prefix)).Select(p => p.ProposalCode).ToListAsync(ct);
        var next = last.Select(c => int.TryParse(c[prefix.Length..], NumberStyles.None, CultureInfo.InvariantCulture, out var n) ? n : 0).DefaultIfEmpty(0).Max() + 1;
        return string.Create(CultureInfo.InvariantCulture, $"{prefix}{next:0000}");
    }

    private static void ValidateContent(CreateProposalRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.Title) || req.Title.Trim().Length < 6 || req.Title.Length > 300)
            throw ApiException.BadRequest("عنوان المقترح يجب أن يكون بين 6 و300 حرف.");
        if (string.IsNullOrWhiteSpace(req.ImplementationMechanism) || req.ImplementationMechanism.Trim().Length < 20)
            throw ApiException.BadRequest("آلية التطبيق يجب ألا تقل عن 20 حرفاً.");
        if (string.IsNullOrWhiteSpace(req.SubmissionReasons) || req.SubmissionReasons.Trim().Length < 20)
            throw ApiException.BadRequest("أسباب التقديم يجب ألا تقل عن 20 حرفاً.");
        if (req.ImplementationMechanism.Length > 8000 || req.SubmissionReasons.Length > 8000)
            throw ApiException.BadRequest("النص طويل جداً.");
    }

    private ContentResult Html(string html)
    {
        // Printable document: inline styles only, no scripts, nothing fetched from elsewhere.
        Response.Headers.ContentSecurityPolicy = "default-src 'none'; style-src 'unsafe-inline'; img-src data:; frame-ancestors 'none'";
        return Content(html, "text/html; charset=utf-8");
    }
}
