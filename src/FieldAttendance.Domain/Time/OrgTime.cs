namespace FieldAttendance.Domain.Time;

/// <summary>
/// The organisation's working clock.
///
/// Two separate ideas, deliberately kept apart:
///   • storage is always UTC — an instant means the same thing everywhere;
///   • business time is the organisation's local zone, because a shift starting
///     "08:00" means eight in the morning where people actually work.
///
/// The zone is configured once at startup (Attendance:TimeZone) instead of being a
/// constant, so the same code serves a branch in another country, and daylight saving
/// is handled by the platform rather than by arithmetic.
///
/// Every calculation truncates to whole minutes first, so seconds can never turn an
/// on-time check-in into a late one at the grace boundary.
/// </summary>
public static class OrgTime
{
    private static TimeZoneInfo _zone = Resolve("Asia/Dubai") ?? TimeZoneInfo.CreateCustomTimeZone("UAE", TimeSpan.FromHours(4), "UAE", "UAE");

    public static TimeZoneInfo Zone => _zone;

    /// <summary>Sets the working zone. Unknown identifiers keep the previous zone rather than crashing at startup.</summary>
    public static bool Configure(string? timeZoneId)
    {
        if (string.IsNullOrWhiteSpace(timeZoneId)) return false;
        var resolved = Resolve(timeZoneId);
        if (resolved is null) return false;
        _zone = resolved;
        return true;
    }

    /// <summary>The offset that applies to a local wall-clock moment, daylight saving included.</summary>
    public static TimeSpan OffsetFor(DateTime localMoment) =>
        _zone.GetUtcOffset(DateTime.SpecifyKind(localMoment, DateTimeKind.Unspecified));

    /// <summary>Turns a local date and time into an exact instant.</summary>
    public static DateTimeOffset At(DateOnly date, TimeOnly time)
    {
        var local = date.ToDateTime(time, DateTimeKind.Unspecified);
        return new DateTimeOffset(local, OffsetFor(local));
    }

    /// <summary>The same instant, expressed in the organisation's local time.</summary>
    public static DateTimeOffset ToLocal(DateTimeOffset value) =>
        TimeZoneInfo.ConvertTime(value, _zone);

    /// <summary>The instant as stored: UTC, never a local offset.</summary>
    public static DateTimeOffset ToStorage(DateTimeOffset value) => value.ToUniversalTime();

    public static DateOnly DateOf(DateTimeOffset value) => DateOnly.FromDateTime(ToLocal(value).DateTime);

    public static TimeOnly TimeOf(DateTimeOffset value) => TimeOnly.FromDateTime(ToLocal(value).DateTime);

    public static DateTimeOffset TruncateToMinute(DateTimeOffset value)
    {
        var local = ToLocal(value);
        var truncated = new DateTime(local.Year, local.Month, local.Day, local.Hour, local.Minute, 0, DateTimeKind.Unspecified);
        return new DateTimeOffset(truncated, local.Offset);
    }

    private static TimeZoneInfo? Resolve(string id)
    {
        // Linux uses IANA ids, Windows its own: try the id, then its counterpart.
        if (TryFind(id, out var direct)) return direct;
        if (TimeZoneInfo.TryConvertIanaIdToWindowsId(id, out var windows) && TryFind(windows, out var byWindows)) return byWindows;
        if (TimeZoneInfo.TryConvertWindowsIdToIanaId(id, out var iana) && TryFind(iana, out var byIana)) return byIana;
        return null;
    }

    private static bool TryFind(string id, out TimeZoneInfo zone)
    {
        try
        {
            zone = TimeZoneInfo.FindSystemTimeZoneById(id);
            return true;
        }
        catch (TimeZoneNotFoundException) { zone = TimeZoneInfo.Utc; return false; }
        catch (InvalidTimeZoneException) { zone = TimeZoneInfo.Utc; return false; }
    }
}

/// <summary>Kept so existing call sites keep working; everything now routes through <see cref="OrgTime"/>.</summary>
public static class UaeTime
{
    public static TimeSpan Offset => OrgTime.OffsetFor(DateTime.UtcNow);

    public static DateTimeOffset At(DateOnly date, TimeOnly time) => OrgTime.At(date, time);

    public static DateTimeOffset ToUae(DateTimeOffset value) => OrgTime.ToLocal(value);

    public static DateOnly DateOf(DateTimeOffset value) => OrgTime.DateOf(value);

    public static DateTimeOffset TruncateToMinute(DateTimeOffset value) => OrgTime.TruncateToMinute(value);
}
