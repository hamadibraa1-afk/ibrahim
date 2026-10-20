using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Tamaneen.Infrastructure.Persistence;
using Tamaneen.SharedKernel;

namespace Tamaneen.Infrastructure.Security;

/// <summary>
/// Checks that the session behind an access token is still active, so signing out, "sign out of all
/// devices" and refresh-token theft detection take effect within seconds instead of at token expiry.
/// Results are cached for 30 seconds per session to keep the database out of the hot path. Used by both
/// the API and the crisis worker.
/// </summary>
public sealed class SessionValidator(TamaneenDbContext db, IMemoryCache cache, IClock clock) : ISessionCheck
{
    private static readonly TimeSpan CacheFor = TimeSpan.FromSeconds(30);

    public async Task<bool> IsActiveAsync(Guid sessionId, CancellationToken cancellationToken)
    {
        if (cache.TryGetValue(Key(sessionId), out bool active))
        {
            return active;
        }

        // Sessions carry no patient data and are outside row-level security, so this works for any caller.
        var session = await db.AuthSessions.AsNoTracking().FirstOrDefaultAsync(s => s.Id == sessionId, cancellationToken).ConfigureAwait(false);
        active = session is not null && session.IsActive(clock);
        cache.Set(Key(sessionId), active, CacheFor);
        return active;
    }

    /// <summary>Called after a revocation so this instance stops accepting the session immediately.</summary>
    public void Forget(Guid sessionId) => cache.Remove(Key(sessionId));

    private static string Key(Guid sessionId) => $"auth-session:{sessionId:N}";
}
