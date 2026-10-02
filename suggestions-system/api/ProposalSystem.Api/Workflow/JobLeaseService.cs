using Microsoft.EntityFrameworkCore;
using ProposalSystem.Api.Data;
using ProposalSystem.Api.Domain;

namespace ProposalSystem.Api.Workflow;

/// <summary>
/// Database-backed lease: whoever holds an unexpired lease runs the job. Works across any number
/// of API instances sharing the database, with no extra infrastructure. A crashed holder simply
/// lets its lease expire and another instance takes over.
/// </summary>
public sealed class JobLeaseService(AppDbContext db, TimeProvider clock)
{
    /// <summary>Identifies this process; stable for its lifetime.</summary>
    public static readonly string InstanceId = Truncate($"{Environment.MachineName}:{Environment.ProcessId}:{Guid.NewGuid():N}", 100);

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..max];

    /// <summary>
    /// Takes or renews the lease. Returns false while another live instance holds it.
    /// The update is a single conditional statement, so two instances can't both win.
    /// </summary>
    public async Task<bool> TryAcquireAsync(string name, TimeSpan duration, string? holder = null, CancellationToken ct = default)
    {
        holder ??= InstanceId;
        var now = clock.GetUtcNow().UtcDateTime;
        var until = now.Add(duration);

        var taken = await db.JobLeases
            .Where(l => l.Name == name && (l.Holder == holder || l.ExpiresAt <= now))
            .ExecuteUpdateAsync(s => s.SetProperty(l => l.Holder, holder).SetProperty(l => l.ExpiresAt, until), ct);
        if (taken == 1)
            return true;

        if (await db.JobLeases.AnyAsync(l => l.Name == name, ct))
            return false; // held by someone else and still valid

        try
        {
            db.JobLeases.Add(new JobLease { Name = name, Holder = holder, ExpiresAt = until });
            await db.SaveChangesAsync(ct);
            return true;
        }
        catch (DbUpdateException)
        {
            // Another instance created the row first; it holds the lease.
            db.ChangeTracker.Clear();
            return false;
        }
    }
}
