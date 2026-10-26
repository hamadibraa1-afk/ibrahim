namespace Tamaneen.SharedKernel.Notifications;

public interface INotifier { Task NotifyAsync(Notification notification, CancellationToken ct); }

public sealed record Notification(Guid RecipientUserId, string Kind, IReadOnlyDictionary<string, string> Data, string? Link = null);
