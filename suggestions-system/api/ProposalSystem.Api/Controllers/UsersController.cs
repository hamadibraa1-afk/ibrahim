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

public sealed record UserDto(
    int Id, string UserCode, string ArabicName, string EnglishName, string Email, UserRole Role,
    string PhoneNumber, string Department, string JobTitle, UserStatus Status, DateTime CreatedAt,
    int? ManagerId, string? ManagerName)
{
    public static UserDto From(User u) => new(
        u.Id, u.UserCode, u.ArabicName, u.EnglishName, u.Email, u.Role, u.PhoneNumber, u.Department,
        u.JobTitle, u.Status, u.CreatedAt, u.ManagerId, u.Manager?.ArabicName);
}

public sealed record CreateUserRequest(
    string UserCode, string ArabicName, string EnglishName, string Email, string InitialPassword,
    UserRole Role, string PhoneNumber, string Department, string JobTitle, int? ManagerId);

public sealed record UpdateUserRequest(
    int Id, string UserCode, string ArabicName, string EnglishName, string Email,
    UserRole Role, string PhoneNumber, string Department, string JobTitle, int? ManagerId);

public sealed record ResetPasswordRequest(string NewPassword);

/// <summary>
/// The internal user management module: accounts, roles and the reporting line used by SLA
/// escalation. It is the single source of identity and permissions — no Active Directory.
/// </summary>
[ApiController]
[Route("api/users")]
[Authorize]
public sealed class UsersController(
    AppDbContext db,
    SessionService sessions,
    CurrentUser me,
    CommitteeProgress committee,
    NotificationService notifications,
    TimeProvider clock) : ControllerBase
{
    [HttpGet]
    [Authorize(Roles = nameof(UserRole.Admin))]
    public async Task<List<UserDto>> GetAll(CancellationToken ct) =>
        (await db.Users.Include(u => u.Manager).OrderBy(u => u.ArabicName).ToListAsync(ct)).Select(UserDto.From).ToList();

    /// <summary>Department names for the list filters; reviewers need them too.</summary>
    [HttpGet("departments")]
    public async Task<List<string>> Departments(CancellationToken ct) =>
        await db.Users.Where(u => u.Department != "").Select(u => u.Department).Distinct().OrderBy(d => d).ToListAsync(ct);

    [HttpGet("{id:int}")]
    [Authorize(Roles = nameof(UserRole.Admin))]
    public async Task<UserDto> Get(int id, CancellationToken ct) => UserDto.From(await Load(id, ct));

    [HttpPost]
    [Authorize(Roles = nameof(UserRole.Admin))]
    public async Task<UserDto> Create(CreateUserRequest req, CancellationToken ct)
    {
        Validate(req.UserCode, req.ArabicName, req.Email);
        if (PasswordHasher.ValidatePolicy(req.InitialPassword) is { } pwdError)
            throw ApiException.BadRequest(pwdError);
        var code = req.UserCode.Trim();
        if (await db.Users.AnyAsync(u => u.UserCode == code, ct))
            throw ApiException.Conflict("الرقم الوظيفي مستخدم بالفعل.");
        await ValidateManagerAsync(null, req.ManagerId, ct);

        var user = new User
        {
            UserCode = code,
            ArabicName = req.ArabicName.Trim(),
            EnglishName = (req.EnglishName ?? "").Trim(),
            Email = req.Email.Trim(),
            Role = req.Role,
            PhoneNumber = (req.PhoneNumber ?? "").Trim(),
            Department = (req.Department ?? "").Trim(),
            JobTitle = (req.JobTitle ?? "").Trim(),
            ManagerId = req.ManagerId,
            PasswordHash = PasswordHasher.Hash(req.InitialPassword),
            CreatedAt = clock.GetUtcNow().UtcDateTime,
        };
        db.Users.Add(user);
        await db.SaveChangesAsync(ct);
        return UserDto.From(await Load(user.Id, ct));
    }

    [HttpPut("{id:int}")]
    [Authorize(Roles = nameof(UserRole.Admin))]
    public async Task<UserDto> Update(int id, UpdateUserRequest req, CancellationToken ct)
    {
        Validate(req.UserCode, req.ArabicName, req.Email);
        var user = await Load(id, ct);
        var code = req.UserCode.Trim();
        if (await db.Users.AnyAsync(u => u.UserCode == code && u.Id != id, ct))
            throw ApiException.Conflict("الرقم الوظيفي مستخدم بالفعل.");
        if (user.Role == UserRole.Admin && req.Role != UserRole.Admin && await IsLastActiveAdminAsync(id, ct))
            throw ApiException.BadRequest("لا يمكن تغيير دور آخر مدير نظام نشط.");
        await ValidateManagerAsync(id, req.ManagerId, ct);

        var roleChanged = user.Role != req.Role;
        user.UserCode = code;
        user.ArabicName = req.ArabicName.Trim();
        user.EnglishName = (req.EnglishName ?? "").Trim();
        user.Email = req.Email.Trim();
        user.Role = req.Role;
        user.PhoneNumber = (req.PhoneNumber ?? "").Trim();
        user.Department = (req.Department ?? "").Trim();
        user.JobTitle = (req.JobTitle ?? "").Trim();
        user.ManagerId = req.ManagerId;
        if (roleChanged && req.Role != UserRole.CommitteeMember)
            await committee.ReleaseSeatsAsync(user, await Me(ct), "انتقل من عضوية اللجنة", clock.GetUtcNow().UtcDateTime, ct);
        await db.SaveChangesAsync(ct);
        await notifications.DispatchAsync(ct);

        // A role change takes effect at once: the old sessions carry the old role claim.
        if (roleChanged && id != me.Id)
            await sessions.RevokeAllAsync(id, null, ct);
        return UserDto.From(await Load(id, ct));
    }

    [HttpPost("{id:int}/suspend")]
    [Authorize(Roles = nameof(UserRole.Admin))]
    public async Task<UserDto> Suspend(int id, CancellationToken ct)
    {
        if (id == me.Id)
            throw ApiException.BadRequest("لا يمكنك إيقاف حسابك.");
        var user = await Load(id, ct);
        if (user.Role == UserRole.Admin && await IsLastActiveAdminAsync(id, ct))
            throw ApiException.BadRequest("لا يمكن إيقاف آخر مدير نظام نشط.");
        user.Status = UserStatus.Suspended;
        await committee.ReleaseSeatsAsync(user, await Me(ct), "أُوقف الحساب", clock.GetUtcNow().UtcDateTime, ct);
        await db.SaveChangesAsync(ct);
        await notifications.DispatchAsync(ct);
        await sessions.RevokeAllAsync(id, null, ct);
        return UserDto.From(user);
    }

    [HttpPost("{id:int}/activate")]
    [Authorize(Roles = nameof(UserRole.Admin))]
    public async Task<UserDto> Activate(int id, CancellationToken ct)
    {
        var user = await Load(id, ct);
        user.Status = UserStatus.Active;
        user.FailedLoginCount = 0;
        user.LockoutEndAt = null;
        await db.SaveChangesAsync(ct);
        return UserDto.From(user);
    }

    [HttpPost("{id:int}/reset-password")]
    [Authorize(Roles = nameof(UserRole.Admin))]
    public async Task<IActionResult> ResetPassword(int id, ResetPasswordRequest req, CancellationToken ct)
    {
        if (PasswordHasher.ValidatePolicy(req.NewPassword) is { } error)
            throw ApiException.BadRequest(error);
        var user = await Load(id, ct);
        user.PasswordHash = PasswordHasher.Hash(req.NewPassword);
        user.FailedLoginCount = 0;
        user.LockoutEndAt = null;
        await db.SaveChangesAsync(ct);
        await sessions.RevokeAllAsync(id, id == me.Id ? me.SessionId : null, ct);
        return NoContent();
    }

    /// <summary>Accounts with history are suspended, not deleted, so the audit trail keeps its actors.</summary>
    [HttpDelete("{id:int}")]
    [Authorize(Roles = nameof(UserRole.Admin))]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        if (id == me.Id)
            throw ApiException.BadRequest("لا يمكنك حذف حسابك.");
        var user = await Load(id, ct);
        var referenced = await db.Proposals.AnyAsync(p => p.SubmitterId == id || p.OwnerId == id || p.ExecutiveDecisionById == id || p.EscalatedToId == id, ct)
                         || await db.CommitteeVotes.AnyAsync(v => v.MemberId == id, ct)
                         || await db.AuditLogs.AnyAsync(a => a.ActorId == id, ct)
                         || await db.Users.AnyAsync(u => u.ManagerId == id, ct);
        if (referenced)
            throw ApiException.Conflict("لا يمكن حذف مستخدم له سجل في النظام أو موظفون يتبعونه. استخدم الإيقاف بدلاً من ذلك.");
        db.Users.Remove(user);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    private async Task<User> Me(CancellationToken ct) => await db.Users.FirstAsync(u => u.Id == me.Id, ct);

    private async Task<User> Load(int id, CancellationToken ct) =>
        await db.Users.Include(u => u.Manager).FirstOrDefaultAsync(u => u.Id == id, ct) ?? throw ApiException.NotFound("المستخدم غير موجود.");

    private Task<bool> IsLastActiveAdminAsync(int id, CancellationToken ct) =>
        db.Users.AllAsync(u => u.Id == id || u.Role != UserRole.Admin || u.Status != UserStatus.Active, ct);

    /// <summary>The manager must exist and must not create a loop in the reporting line.</summary>
    private async Task ValidateManagerAsync(int? userId, int? managerId, CancellationToken ct)
    {
        if (managerId is null)
            return;
        if (managerId == userId)
            throw ApiException.BadRequest("لا يمكن أن يكون المستخدم مديراً لنفسه.");

        var managers = await db.Users.Select(u => new { u.Id, u.ManagerId }).ToDictionaryAsync(u => u.Id, u => u.ManagerId, ct);
        if (!managers.ContainsKey(managerId.Value))
            throw ApiException.BadRequest("المدير المباشر المحدد غير موجود.");

        var seen = new HashSet<int>();
        for (int? cursor = managerId; cursor is { } c && seen.Add(c); cursor = managers.GetValueOrDefault(c))
        {
            if (c == userId)
                throw ApiException.BadRequest("هذا الاختيار يُنشئ حلقة في التسلسل الإداري.");
        }
    }

    private static void Validate(string? userCode, string? arabicName, string? email)
    {
        if (string.IsNullOrWhiteSpace(userCode) || string.IsNullOrWhiteSpace(arabicName) || string.IsNullOrWhiteSpace(email))
            throw ApiException.BadRequest("الرقم الوظيفي والاسم والبريد الإلكتروني حقول إلزامية.");
        if (!email.Contains('@', StringComparison.Ordinal))
            throw ApiException.BadRequest("البريد الإلكتروني غير صالح.");
    }
}
