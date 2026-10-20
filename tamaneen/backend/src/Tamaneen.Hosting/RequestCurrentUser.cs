using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Tamaneen.SharedKernel;

namespace Tamaneen.Hosting;

/// <summary>
/// The caller of the current request, read from the validated access token (sub, role).
/// Code that runs on behalf of the platform itself (background loops, the service-to-service evaluate
/// call) switches to a service identity explicitly with <see cref="ActAsService"/>; there is no implicit
/// fallback, so an unauthenticated request never gets service rights.
/// </summary>
public sealed class RequestCurrentUser : ICurrentUser
{
    private readonly IHttpContextAccessor _accessor;
    private StaticCurrentUser? _override;

    public RequestCurrentUser(IHttpContextAccessor accessor) => _accessor = accessor;

    private ClaimsPrincipal? Principal =>
        _accessor.HttpContext?.User is { Identity.IsAuthenticated: true } user ? user : null;

    public Guid? UserId => _override is not null ? _override.UserId : Guid.TryParse(Principal?.FindFirstValue(TamaneenClaims.Subject), out var id) ? id : null;

    public string? Role => _override is not null ? _override.Role : Principal?.FindFirstValue(TamaneenClaims.Role) is { } role && AppRoles.IsKnown(role) && !AppRoles.IsService(role) ? role : null;

    public Guid? PatientId => _override is not null ? _override.PatientId : Role == AppRoles.Patient ? UserId : null;

    public string Language => _override?.Language ?? (_accessor.HttpContext is { } ctx ? LocalizationSetup.RequestLanguage(ctx) : LocalizationSetup.CurrentLanguage());

    /// <summary>Switches this scope to a platform service identity. Must be called before the first database access of the scope.</summary>
    public void ActAsService(Guid serviceId, string serviceRole)
    {
        if (!AppRoles.IsService(serviceRole))
        {
            throw new ArgumentException($"{serviceRole} is not a service role.", nameof(serviceRole));
        }

        _override = StaticCurrentUser.ForStaff(serviceId, serviceRole);
    }

    /// <summary>
    /// Switches this scope to a user whose credentials the sign-in flow has just verified (there is no token
    /// yet). Only the auth flows call this, in a scope of their own, before the scope's first database access.
    /// </summary>
    public void ActAsVerifiedUser(Guid userId, string role, string language)
    {
        if (!AppRoles.IsKnown(role) || AppRoles.IsService(role))
        {
            throw new ArgumentException($"{role} is not a user role.", nameof(role));
        }

        _override = role == AppRoles.Patient ? StaticCurrentUser.ForPatient(userId, language) : StaticCurrentUser.ForStaff(userId, role, language);
    }
}
