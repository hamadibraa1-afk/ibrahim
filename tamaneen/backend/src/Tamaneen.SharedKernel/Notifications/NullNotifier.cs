namespace Tamaneen.SharedKernel.Notifications;

/// <summary>Drops every notification. Registered with TryAdd so the notification service's own registration wins.</summary>
public sealed class NullNotifier : INotifier
{
    public Task NotifyAsync(Notification notification, CancellationToken ct) => Task.CompletedTask;
}
