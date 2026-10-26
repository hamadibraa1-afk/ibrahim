namespace Tamaneen.SharedKernel.Notifications;

/// <summary>Sends nothing. The API host registers it with TryAdd, so the real notification service replaces it.</summary>
public sealed class NullNotifier : INotifier
{
    public Task NotifyAsync(Notification notification, CancellationToken ct) => Task.CompletedTask;
}
