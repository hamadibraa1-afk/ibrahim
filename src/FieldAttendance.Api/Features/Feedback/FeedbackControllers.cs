using System.ComponentModel.DataAnnotations;
using FieldAttendance.Api.Common;
using FieldAttendance.Api.Data;
using FieldAttendance.Domain.Common;
using FieldAttendance.Domain.Entities;
using FieldAttendance.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

namespace FieldAttendance.Api.Features.Feedback;

public sealed record SubmitFeedbackRequest(
    [Required] string Kind,
    [Required, MaxLength(120)] string Name,
    [Required, MaxLength(20)] string Phone,
    [EmailAddress, MaxLength(254)] string? Email,
    [Required, MaxLength(1000)] string Message);

public sealed record FeedbackReceiptDto(string Reference);

public sealed record FeedbackDto(Guid Id, string Reference, string Kind, string Status, DateTimeOffset SubmittedAt,
    string LocationName, string? EmployeeName, string CustomerName, string CustomerPhone, string? CustomerEmail,
    string Message, string? AssignedToName, string? ResolutionNote, DateTimeOffset? ClosedAt, int? HandlingMinutes);

public sealed record NoteRequest(string? Note);

public sealed record CloseRequest([Required] string Note);

public sealed record AssignRequest(Guid UserId);

/// <summary>Anonymous endpoint behind the site QR code: the customer files a complaint or a suggestion.</summary>
[ApiController]
[Route("api/public/r/{token:guid}/feedback")]
[AllowAnonymous]
[EnableRateLimiting("public")]
public sealed class PublicFeedbackController(AppDbContext db, IClock clock) : ControllerBase
{
    [HttpPost]
    public async Task<ActionResult<FeedbackReceiptDto>> Submit(Guid token, SubmitFeedbackRequest r, CancellationToken ct)
    {
        if (!Enum.TryParse<FeedbackKind>(r.Kind, true, out var kind))
            throw new DomainException("feedback.invalid_kind", "Unknown feedback kind.");

        var location = await db.Locations.AsNoTracking()
            .SingleOrDefaultAsync(l => l.QrToken == token && l.IsActive && l.Kind == LocationKind.Field, ct)
            ?? throw new DomainException("rating.invalid_code", "This code is not valid.");

        var now = clock.Now;
        var onExit = db.TemporaryExits.Where(e => e.ReturnAt == null).Select(e => e.AttendanceRecordId);
        var present = await db.AttendanceRecords.AsNoTracking()
            .Where(x => x.LocationId == location.Id && x.CheckInAt != null && x.CheckOutAt == null && !onExit.Contains(x.Id))
            .Select(x => new { x.Id, x.EmployeeId }).ToListAsync(ct);

        // Attach the employee only when exactly one person is on duty; otherwise the item belongs to the site.
        var single = present.Count == 1 ? present[0] : null;

        var feedback = new Domain.Entities.Feedback(await NextReferenceAsync(now.Year, ct), kind, location.Id,
            single?.EmployeeId, single?.Id, r.Name, r.Phone, r.Email, r.Message, now);
        db.Feedback.Add(feedback);
        await db.SaveChangesAsync(ct);

        return new FeedbackReceiptDto(feedback.Reference);
    }

    private async Task<string> NextReferenceAsync(int year, CancellationToken ct)
    {
        var prefix = $"SCI-{year}-";
        var used = await db.Feedback.CountAsync(f => f.Reference.StartsWith(prefix), ct);
        return Domain.Entities.Feedback.BuildReference(year, used + 1);
    }
}

/// <summary>Supervisor inbox for complaints and suggestions, scoped to the sites they own.</summary>
[ApiController]
[Route("api/feedback")]
[Authorize(Policy = Policies.Read)]
public sealed class FeedbackController(AppDbContext db, AccessScope scope, ICurrentUser me, IClock clock) : ControllerBase
{
    [HttpGet]
    public async Task<IReadOnlyList<FeedbackDto>> List([FromQuery] string? kind, [FromQuery] string? status, CancellationToken ct)
    {
        var query = db.Feedback.AsNoTracking().Where(f => f.IsActive);
        if (Enum.TryParse<FeedbackKind>(kind, true, out var k)) query = query.Where(f => f.Kind == k);
        if (Enum.TryParse<FeedbackStatus>(status, true, out var s)) query = query.Where(f => f.Status == s);

        var scoped = await scope.LocationIdsAsync(ct);
        if (scoped is not null) query = query.Where(f => scoped.Contains(f.LocationId));

        var list = await query.OrderByDescending(f => f.SubmittedAt).Take(500).ToListAsync(ct);
        var locations = await db.Locations.AsNoTracking().ToDictionaryAsync(l => l.Id, l => l.NameAr, ct);
        var users = await db.Users.AsNoTracking().ToDictionaryAsync(u => u.Id, u => u.FullName, ct);

        return list.Select(f => new FeedbackDto(f.Id, f.Reference, f.Kind.ToString(), f.Status.ToString(), f.SubmittedAt,
            locations.GetValueOrDefault(f.LocationId, "?"),
            f.EmployeeId is { } e ? users.GetValueOrDefault(e) : null,
            f.CustomerName, f.CustomerPhone, f.CustomerEmail, f.Message,
            f.AssignedToId is { } a ? users.GetValueOrDefault(a) : null,
            f.ResolutionNote, f.ClosedAt, f.HandlingMinutes)).ToList();
    }

    [HttpGet("counts")]
    public async Task<IReadOnlyDictionary<string, int>> Counts(CancellationToken ct)
    {
        var scoped = await scope.LocationIdsAsync(ct);
        var query = db.Feedback.AsNoTracking().Where(f => f.IsActive && f.Status != FeedbackStatus.Closed);
        if (scoped is not null) query = query.Where(f => scoped.Contains(f.LocationId));
        var open = await query.ToListAsync(ct);
        return new Dictionary<string, int>
        {
            ["complaints"] = open.Count(f => f.Kind == FeedbackKind.Complaint),
            ["suggestions"] = open.Count(f => f.Kind == FeedbackKind.Suggestion),
            ["new"] = open.Count(f => f.Status == FeedbackStatus.New),
        };
    }

    [HttpPost("{id:guid}/take")]
    [Authorize(Policy = Policies.Manage)]
    public async Task<IActionResult> Take(Guid id, CancellationToken ct)
    {
        var f = await Find(id, ct);
        f.Assign(me.RequiredId);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    [HttpPost("{id:guid}/assign")]
    [Authorize(Policy = Policies.Manage)]
    public async Task<IActionResult> Assign(Guid id, AssignRequest r, CancellationToken ct)
    {
        var f = await Find(id, ct);
        if (!await db.Users.AnyAsync(u => u.Id == r.UserId && u.IsActive, ct))
            throw new DomainException("employee.not_found", "Account not found.");
        f.Assign(r.UserId);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    [HttpPost("{id:guid}/escalate")]
    [Authorize(Policy = Policies.Manage)]
    public async Task<IActionResult> Escalate(Guid id, NoteRequest r, CancellationToken ct)
    {
        var f = await Find(id, ct);
        f.Escalate(me.RequiredId, r.Note);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    [HttpPost("{id:guid}/close")]
    [Authorize(Policy = Policies.Manage)]
    public async Task<IActionResult> Close(Guid id, CloseRequest r, CancellationToken ct)
    {
        var f = await Find(id, ct);
        f.Close(me.RequiredId, r.Note, clock.Now);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    [HttpPost("{id:guid}/reopen")]
    [Authorize(Policy = Policies.Manage)]
    public async Task<IActionResult> Reopen(Guid id, CancellationToken ct)
    {
        var f = await Find(id, ct);
        f.Reopen();
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    private async Task<Domain.Entities.Feedback> Find(Guid id, CancellationToken ct) =>
        await db.Feedback.SingleOrDefaultAsync(f => f.Id == id && f.IsActive, ct)
        ?? throw new DomainException("feedback.not_found", "Item not found.");
}
