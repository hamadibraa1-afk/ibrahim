using Tamaneen.SharedKernel.Notifications;

namespace Tamaneen.Api;

/// <summary>Placeholder until the notification service is registered (TryAdd, so the real one wins).</summary>
public sealed class NullNotifier : INotifier
{
    public Task NotifyAsync(Notification notification, CancellationToken ct) => Task.CompletedTask;
}
