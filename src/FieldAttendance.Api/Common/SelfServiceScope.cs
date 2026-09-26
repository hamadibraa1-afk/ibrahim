using FieldAttendance.Api.Data;
using FieldAttendance.Domain.Common;
using FieldAttendance.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace FieldAttendance.Api.Common;

/// <summary>
/// Whether the signed-in user is someone the organisation keeps attendance for, decided from
/// their records rather than their role: an HR manager with a profile and a schedule is an
/// employee like any other, and an administrator account with neither is not.
///
/// Two kinds of record count, because the two workforces are modelled differently. Office staff
/// have an <see cref="Domain.Entities.EmployeeProfile"/>; field collectors have no HR profile at
/// all and exist only through their site assignments. Requiring a profile alone would lock
/// every collector out of their own check-in.
/// </summary>
public sealed class SelfServiceScope(AppDbContext db, ICurrentUser me, IClock clock)
{
    public async Task<bool> HasEmploymentRecordAsync(CancellationToken ct)
    {
        var id = me.RequiredId;
        var status = await db.EmployeeProfiles.AsNoTracking()
            .Where(p => p.UserId == id).Select(p => (EmploymentStatus?)p.Status).SingleOrDefaultAsync(ct);

        // A profile, when there is one, is the authority: a suspended or ended employee keeps
        // their old site assignment rows, and those must not reopen self-service.
        if (status is { } s) return s is EmploymentStatus.Active or EmploymentStatus.OnLeave;

        var today = clock.Today;
        return await db.Assignments.AsNoTracking()
            .AnyAsync(a => a.EmployeeId == id && a.IsActive && (a.EndDate == null || a.EndDate >= today), ct);
    }

    public async Task EnsureEmploymentRecordAsync(CancellationToken ct)
    {
        if (!await HasEmploymentRecordAsync(ct))
            throw new DomainException("self.no_employment_record",
                "This account has no employment record, so it has no attendance or leave of its own.");
    }
}
