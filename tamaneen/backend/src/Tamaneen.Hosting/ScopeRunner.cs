using Microsoft.Extensions.DependencyInjection;
using Tamaneen.SharedKernel;

namespace Tamaneen.Hosting;

/// <summary>
/// Runs work in a fresh DI scope under another identity: a service role, or a user the sign-in flow has
/// just verified. A new scope means a new DbContext and connection, so the database session context
/// (and therefore row-level security) is set for that identity and never leaks back into the request.
/// </summary>
public sealed class ScopeRunner(IServiceScopeFactory scopes)
{
    public async Task<T> AsServiceAsync<T>(Guid serviceId, string serviceRole, Func<IServiceProvider, Task<T>> work)
    {
        await using var scope = scopes.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<RequestCurrentUser>().ActAsService(serviceId, serviceRole);
        return await work(scope.ServiceProvider).ConfigureAwait(false);
    }

    public async Task<T> AsUserAsync<T>(Guid userId, string role, string language, Func<IServiceProvider, Task<T>> work)
    {
        await using var scope = scopes.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<RequestCurrentUser>().ActAsVerifiedUser(userId, role, language);
        return await work(scope.ServiceProvider).ConfigureAwait(false);
    }

    public Task AsUserAsync(Guid userId, string role, string language, Func<IServiceProvider, Task> work) =>
        AsUserAsync<bool>(userId, role, language, async sp =>
        {
            await work(sp).ConfigureAwait(false);
            return true;
        });
}
