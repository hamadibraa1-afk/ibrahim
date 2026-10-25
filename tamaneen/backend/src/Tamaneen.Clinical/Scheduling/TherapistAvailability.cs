using System.Globalization;
using System.Text.Json;
using Tamaneen.SharedKernel;

namespace Tamaneen.Clinical.Scheduling;

/// <summary>One period of the usual week (S19), in the therapist's time zone: Sunday 10:00–13:00.</summary>
public sealed record WeeklyPeriod(int Day, string Start, string End)
{
    public TimeOnly StartTime => TimeOnly.ParseExact(Start, "HH:mm", CultureInfo.InvariantCulture);

    public TimeOnly EndTime => TimeOnly.ParseExact(End, "HH:mm", CultureInfo.InvariantCulture);
}

/// <summary>Leave or an absence (S19): no times are generated from the pattern inside it.</summary>
public sealed record AvailabilityBreak(Guid Id, string Kind, DateTimeOffset StartsAtUtc, DateTimeOffset EndsAtUtc);

/// <summary>
/// The usual week, the gap between sessions and the exceptions of one therapist (S19, SVC-49). Saving it
/// generates the bookable times of the next eight weeks (<see cref="AvailabilityPlanner"/>); the session length
/// itself lives on <see cref="TherapistProfile.SessionMinutes"/>. No patient data, so no row-level security:
/// the endpoints let a therapist change only their own row.
/// </summary>
public sealed class TherapistAvailability
{
    public static readonly IReadOnlyList<int> BufferOptions = [0, 5, 10, 15, 20, 30];

    public static readonly IReadOnlyList<string> ExceptionKinds = ["leave", "absence"];

    public const int MaxPeriodsPerDay = 6;

    public const int MaxExceptions = 50;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private TherapistAvailability(Guid therapistUserId)
    {
        TherapistUserId = therapistUserId;
        WeeklyJson = "[]";
        ExceptionsJson = "[]";
        BufferMinutes = 10;
    }

    // EF Core
    private TherapistAvailability()
    {
        WeeklyJson = "[]";
        ExceptionsJson = "[]";
    }

    public Guid TherapistUserId { get; private set; }

    public string WeeklyJson { get; private set; }

    public string ExceptionsJson { get; private set; }

    public int BufferMinutes { get; private set; }

    public DateTimeOffset? UpdatedAtUtc { get; private set; }

    public IReadOnlyList<WeeklyPeriod> Weekly => JsonSerializer.Deserialize<List<WeeklyPeriod>>(WeeklyJson, Json) ?? [];

    public IReadOnlyList<AvailabilityBreak> Exceptions => JsonSerializer.Deserialize<List<AvailabilityBreak>>(ExceptionsJson, Json) ?? [];

    public static TherapistAvailability Empty(Guid therapistUserId) => new(therapistUserId);

    /// <summary>Validates and stores the pattern; field errors use keys like <c>weekly.0</c> and <c>exceptions.1</c>.</summary>
    public void Set(IReadOnlyList<WeeklyPeriod> weekly, IReadOnlyList<AvailabilityBreak> exceptions, int bufferMinutes, IClock clock)
    {
        var errors = Validate(weekly, exceptions, bufferMinutes);
        if (errors.Count > 0)
        {
            throw new DomainException(ErrorCodes.Validation, "Invalid availability.").With("errors", errors);
        }

        WeeklyJson = JsonSerializer.Serialize(weekly.OrderBy(p => p.Day).ThenBy(p => p.Start, StringComparer.Ordinal).ToList(), Json);
        ExceptionsJson = JsonSerializer.Serialize(exceptions.OrderBy(e => e.StartsAtUtc).ToList(), Json);
        BufferMinutes = bufferMinutes;
        UpdatedAtUtc = clock.UtcNow;
    }

    public static Dictionary<string, string[]> Validate(IReadOnlyList<WeeklyPeriod> weekly, IReadOnlyList<AvailabilityBreak> exceptions, int bufferMinutes)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
        if (!BufferOptions.Contains(bufferMinutes))
        {
            errors["bufferMinutes"] = ["invalid"];
        }

        var valid = new List<(int Index, WeeklyPeriod Period)>();
        for (var i = 0; i < weekly.Count; i++)
        {
            var p = weekly[i];
            if (p.Day is < 0 or > 6 || !IsTime(p.Start) || !IsTime(p.End))
            {
                errors[$"weekly.{i}"] = ["invalid"];
            }
            else if (p.EndTime <= p.StartTime)
            {
                errors[$"weekly.{i}"] = ["end_before_start"];
            }
            else
            {
                valid.Add((i, p));
            }
        }

        foreach (var day in valid.GroupBy(v => v.Period.Day))
        {
            var ordered = day.OrderBy(v => v.Period.StartTime).ToList();
            if (ordered.Count > MaxPeriodsPerDay)
            {
                errors[$"weekly.{ordered[MaxPeriodsPerDay].Index}"] = ["invalid"];
            }

            for (var k = 1; k < ordered.Count; k++)
            {
                if (ordered[k].Period.StartTime < ordered[k - 1].Period.EndTime)
                {
                    errors[$"weekly.{ordered[k].Index}"] = ["overlap"];
                }
            }
        }

        if (exceptions.Count > MaxExceptions)
        {
            errors["exceptions"] = ["invalid"];
        }

        for (var i = 0; i < exceptions.Count; i++)
        {
            var e = exceptions[i];
            if (!ExceptionKinds.Contains(e.Kind))
            {
                errors[$"exceptions.{i}"] = ["invalid"];
            }
            else if (e.EndsAtUtc <= e.StartsAtUtc)
            {
                errors[$"exceptions.{i}"] = ["end_before_start"];
            }
        }

        return errors;
    }

    private static bool IsTime(string? value) =>
        value is { Length: 5 } && TimeOnly.TryParseExact(value, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out _);
}
