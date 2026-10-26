namespace Tamaneen.SharedKernel.Notifications;

/// <summary>Does nothing. Registered with TryAdd so the API host's real notifier wins wherever it is present.</summary>
public sealed class NullNotifier : INotifier
{
    public Task NotifyAsync(Notification notification, CancellationToken ct) => Task.CompletedTask;
}
