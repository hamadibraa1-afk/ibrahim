using System.Reflection;
using System.Security.Claims;
using FieldAttendance.Api.Common;
using FieldAttendance.Api.Features.Attendance;
using FieldAttendance.Api.Features.Employees;
using FieldAttendance.Api.Features.Locations;
using FieldAttendance.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace FieldAttendance.Api.Tests;

/// <summary>
/// Evaluates the real policies against the [Authorize] attributes on a controller action, the way
/// the framework does: the class-level policy and the action-level policy must both pass.
/// These guard the gap where a screen is reachable in the UI but its endpoint refuses the role,
/// which shows up as an empty page rather than an error.
/// </summary>
public sealed class EndpointAccessTests
{
    private static readonly IAuthorizationService Authorization = new ServiceCollection()
        .AddLogging().AddAppAuthorization().BuildServiceProvider().GetRequiredService<IAuthorizationService>();

    private static async Task<bool> CanCall(UserRole role, Type controller, string action)
    {
        var method = controller.GetMethod(action, BindingFlags.Public | BindingFlags.Instance)
                     ?? throw new InvalidOperationException($"{controller.Name}.{action} not found");
        var policies = controller.GetCustomAttributes<AuthorizeAttribute>()
            .Concat(method.GetCustomAttributes<AuthorizeAttribute>())
            .Select(a => a.Policy).OfType<string>().ToList();
        Assert.NotEmpty(policies);

        var user = new ClaimsPrincipal(new ClaimsIdentity([new Claim(AppClaims.UserId, Guid.NewGuid().ToString()), new Claim(AppClaims.Role, role.ToString())], "test", AppClaims.Name, AppClaims.Role));
        foreach (var policy in policies)
            if (!(await Authorization.AuthorizeAsync(user, null, policy)).Succeeded) return false;
        return true;
    }

    [Theory]
    [InlineData(UserRole.HrManager)]
    [InlineData(UserRole.HrOfficer)]
    public async Task Hr_can_read_the_attendance_log_that_the_hr_module_shows(UserRole role)
    {
        Assert.True(await CanCall(role, typeof(AttendanceController), nameof(AttendanceController.List)));
        Assert.True(await CanCall(role, typeof(LocationsController), nameof(LocationsController.List)));
        Assert.True(await CanCall(role, typeof(EmployeesController), nameof(EmployeesController.List)));
    }

    [Theory]
    [InlineData(UserRole.HrManager)]
    [InlineData(UserRole.HrOfficer)]
    public async Task Hr_read_access_does_not_extend_to_changing_field_data(UserRole role)
    {
        Assert.False(await CanCall(role, typeof(LocationsController), nameof(LocationsController.Create)));
        Assert.False(await CanCall(role, typeof(EmployeesController), nameof(EmployeesController.Create)));
    }

    [Theory]
    [InlineData(UserRole.Collector)]
    [InlineData(UserRole.Employee)]
    public async Task Self_service_roles_cannot_read_everyones_attendance(UserRole role)
    {
        Assert.False(await CanCall(role, typeof(AttendanceController), nameof(AttendanceController.List)));
        Assert.False(await CanCall(role, typeof(LocationsController), nameof(LocationsController.List)));
    }

    [Theory]
    [InlineData(UserRole.SystemAdmin)]
    [InlineData(UserRole.Supervisor)]
    [InlineData(UserRole.DepartmentManager)]
    public async Task Field_readers_keep_their_access(UserRole role)
    {
        Assert.True(await CanCall(role, typeof(AttendanceController), nameof(AttendanceController.List)));
        Assert.True(await CanCall(role, typeof(LocationsController), nameof(LocationsController.List)));
    }

    /// <summary>Self-service is gated by the caller's records, never by role, so every role reaches it.</summary>
    [Theory]
    [MemberData(nameof(AllRoles))]
    public async Task Every_role_reaches_its_own_self_service(UserRole role)
    {
        Assert.True(await CanCall(role, typeof(MyAttendanceController), nameof(MyAttendanceController.Today)));
        Assert.True(await CanCall(role, typeof(FieldAttendance.Api.Features.Leaves.MyLeavesController),
            nameof(FieldAttendance.Api.Features.Leaves.MyLeavesController.Submit)));
    }

    public static TheoryData<UserRole> AllRoles()
    {
        var data = new TheoryData<UserRole>();
        foreach (var role in Enum.GetValues<UserRole>()) data.Add(role);
        return data;
    }
}
