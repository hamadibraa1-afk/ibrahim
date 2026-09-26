using FieldAttendance.Domain.Enums;

namespace FieldAttendance.Api.Common;

/// <summary>
/// The role → policy map lives here rather than inline in Program.cs so tests can build the
/// exact same policies and check which roles reach which endpoint.
/// </summary>
public static class AuthorizationSetup
{
    public static IServiceCollection AddAppAuthorization(this IServiceCollection services)
    {
        services.AddAuthorizationBuilder()
            .AddPolicy(Policies.Admin, p => p.RequireRole(nameof(UserRole.SystemAdmin)))
            .AddPolicy(Policies.Manage, p => p.RequireRole(nameof(UserRole.SystemAdmin), nameof(UserRole.Supervisor)))
            .AddPolicy(Policies.Read, p => p.RequireRole(nameof(UserRole.SystemAdmin), nameof(UserRole.Supervisor), nameof(UserRole.DepartmentManager)))
            .AddPolicy(Policies.ReadAny, p => p.RequireRole(nameof(UserRole.SystemAdmin), nameof(UserRole.Supervisor),
                nameof(UserRole.DepartmentManager), nameof(UserRole.HrManager), nameof(UserRole.HrOfficer)))
            .AddPolicy(Policies.SelfService, p => p.RequireAuthenticatedUser().RequireClaim(AppClaims.UserId))
            .AddPolicy(HrPolicies.Manage, p => p.RequireRole(nameof(UserRole.SystemAdmin), nameof(UserRole.HrManager)))
            .AddPolicy(HrPolicies.Read, p => p.RequireRole(nameof(UserRole.SystemAdmin), nameof(UserRole.HrManager),
                nameof(UserRole.HrOfficer), nameof(UserRole.DepartmentManager)))
            .AddPolicy(HrPolicies.Self, p => p.RequireRole(nameof(UserRole.SystemAdmin), nameof(UserRole.HrManager),
                nameof(UserRole.HrOfficer), nameof(UserRole.DepartmentManager), nameof(UserRole.Employee)));
        return services;
    }
}
