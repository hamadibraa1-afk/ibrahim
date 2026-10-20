using System.Security.Claims;
using Tamaneen.Api.Localization;
using Tamaneen.SharedKernel;

namespace Tamaneen.Api.Security;

/// <summary>Reads the caller from the validated access token. Claims: sub (user id), role, pid (pseudonym).</summary>
public sealed class HttpCurrentUser : ICurrentUser
{
    private readonly IHttpContextAccessor _accessor;

    public HttpCurrentUser(IHttpContextAccessor accessor) => _accessor = accessor;

    private ClaimsPrincipal? Principal =>
        _accessor.HttpContext?.User is { Identity.IsAuthenticated: true } user ? user : null;

    public Guid? UserId => Guid.TryParse(Principal?.FindFirstValue(TamaneenClaims.Subject), out var id) ? id : null;

    public string? Role => Principal?.FindFirstValue(TamaneenClaims.Role) is { } role && AppRoles.IsKnown(role) ? role : null;

    public Guid? PatientId => Role == AppRoles.Patient ? UserId : null;

    public string Language => LocalizationSetup.CurrentLanguage();
}

public static class TamaneenClaims
{
    public const string Subject = "sub";
    public const string Role = "role";
    public const string Pseudonym = "pid";
}
