using System.Security.Claims;
using FieldAttendance.Domain.Time;

namespace FieldAttendance.Api.Common;

public interface IClock
{
    DateTimeOffset Now { get; }
    DateOnly Today { get; }
}

public sealed class SystemClock(TimeProvider time) : IClock
{
    public DateTimeOffset Now => UaeTime.ToUae(time.GetUtcNow());
    public DateOnly Today => UaeTime.DateOf(Now);
}

public interface ICurrentUser
{
    Guid? Id { get; }
    Guid RequiredId { get; }
}

public sealed class HttpCurrentUser(IHttpContextAccessor accessor) : ICurrentUser
{
    public Guid? Id =>
        Guid.TryParse(accessor.HttpContext?.User.FindFirstValue(AppClaims.UserId), out var id) ? id : null;

    public Guid RequiredId => Id ?? throw new UnauthorizedAccessException();
}

public sealed class AttendanceOptions
{
    public double MaxGpsAccuracyMeters { get; init; } = 100;
    public int AutoCloseDelayMinutes { get; init; } = 60;
    public int MinimumOvertimeMinutes { get; init; }
    public int RatingRepeatWindowMinutes { get; init; } = 120;
}

public sealed class JwtOptions
{
    public string Issuer { get; init; } = "FieldAttendance";
    public string Audience { get; init; } = "FieldAttendance.Web";
    public string Key { get; init; } = string.Empty;
    public int ExpiryHours { get; init; } = 8;
}

public static class AppClaims
{
    public const string UserId = "sub";
    public const string Name = "name";
    public const string Role = "role";
}

/// <summary>HR access: reading is open to HR and oversight, changing is not.</summary>
public static class HrPolicies
{
    /// <summary>HR managers and system administrators: profiles, settings, salaries.</summary>
    public const string Manage = nameof(Manage) + "Hr";
    /// <summary>Everyone with an HR view: HR staff, department managers, executives.</summary>
    public const string Read = nameof(Read) + "Hr";
    /// <summary>An office employee acting on their own record.</summary>
    public const string Self = nameof(Self) + "Hr";
}

public static class Policies
{
    /// <summary>System administrator only: user accounts and system-wide settings.</summary>
    public const string Admin = nameof(Admin);
    /// <summary>Anything that changes operational data: administrators and supervisors.</summary>
    public const string Manage = nameof(Manage);
    /// <summary>Read access, including department managers who only review.</summary>
    public const string Read = nameof(Read);
    /// <summary>
    /// Read access shared by both modules: field readers plus HR staff. The HR module shows the same
    /// attendance log, sites and people as the field module, and AccessScope already treats HR as
    /// seeing everyone; this policy lets those requests reach that code.
    /// </summary>
    public const string ReadAny = nameof(ReadAny);
    /// <summary>
    /// The caller acting on their own records under /api/me. Deliberately not a role list: role
    /// decides what you may do to other people's records, never whether you can see your own.
    /// Whether the caller is someone the organisation keeps attendance for is checked against
    /// their records by <see cref="SelfServiceScope"/>, not here.
    /// </summary>
    public const string SelfService = nameof(SelfService);
}

/// <summary>Stable error shape for the Angular client: { code, message, data? }.</summary>
public sealed record ApiError(string Code, string Message, object? Data = null);
