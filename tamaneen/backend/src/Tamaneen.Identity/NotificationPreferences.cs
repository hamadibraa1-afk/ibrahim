using System.Globalization;
using Tamaneen.SharedKernel;

namespace Tamaneen.Identity;

public enum ReminderFrequency
{
    Daily = 0,
    EveryTwoDays = 1,
    Off = 2,
}

/// <summary>How and when a user is notified. Crisis calls ignore all of this, including quiet hours.</summary>
public sealed class NotificationPreferences
{
    private NotificationPreferences(Guid userId) => UserId = userId;

    // EF Core
    private NotificationPreferences()
    {
    }

    public Guid UserId { get; private set; }

    public bool Push { get; private set; } = true;

    public bool Sms { get; private set; }

    public bool Email { get; private set; }

    /// <summary>Notifications say "you have a reminder" instead of their content.</summary>
    public bool NeutralText { get; private set; } = true;

    public bool QuickExit { get; private set; }

    public ReminderFrequency HomeworkReminder { get; private set; } = ReminderFrequency.Daily;

    public TimeOnly ReminderTime { get; private set; } = new(21, 0);

    public TimeOnly QuietFrom { get; private set; } = new(23, 0);

    public TimeOnly QuietTo { get; private set; } = new(8, 0);

    public static NotificationPreferences Default(Guid userId) => new(userId);

    public void Update(bool push, bool sms, bool email, bool neutralText, bool quickExit, ReminderFrequency reminder, string reminderTime, string quietFrom, string quietTo)
    {
        Push = push;
        Sms = sms;
        Email = email;
        NeutralText = neutralText;
        QuickExit = quickExit;
        HomeworkReminder = reminder;
        ReminderTime = ParseTime(reminderTime, nameof(reminderTime));
        QuietFrom = ParseTime(quietFrom, nameof(quietFrom));
        QuietTo = ParseTime(quietTo, nameof(quietTo));
    }

    public bool IsQuiet(TimeOnly local) =>
        QuietFrom <= QuietTo ? local >= QuietFrom && local < QuietTo : local >= QuietFrom || local < QuietTo;

    private static TimeOnly ParseTime(string value, string field) =>
        TimeOnly.TryParseExact(value, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var t)
            ? t
            : throw new DomainException(ErrorCodes.InvalidTime, $"Invalid time for {field}.", value);
}
