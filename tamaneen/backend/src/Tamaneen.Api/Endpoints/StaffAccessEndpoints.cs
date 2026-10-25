using Microsoft.AspNetCore.Http.HttpResults;
using Tamaneen.Infrastructure.Auditing;
using Tamaneen.Infrastructure.Persistence;
using Tamaneen.SharedKernel;

namespace Tamaneen.Api.Endpoints;

/// <summary>
/// S06 /forbidden: the portal reports a page a staff member was not allowed to open, so the attempt is in the
/// audit ledger. The portal's route guards decide; this only records what they refused.
/// </summary>
public static class StaffAccessEndpoints
{
    public const int MaxPathLength = 500;

    public sealed record AccessDeniedRequest(string? Path);

    public static IEndpointRouteBuilder MapStaffAccessEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/v1/staff/access-denied", AccessDeniedAsync)
            .RequireAuthorization(p => p.RequireAuthenticatedUser().RequireRole(AppRoles.Therapist, AppRoles.Supervisor, AppRoles.ClinicalLead, AppRoles.Admin));
        return app;
    }

    internal static async Task<NoContent> AccessDeniedAsync(AccessDeniedRequest request, TamaneenDbContext db, IAuditLog audit, CancellationToken ct)
    {
        var path = request.Path?.Trim();
        if (string.IsNullOrEmpty(path) || !path.StartsWith('/'))
        {
            throw new DomainException(ErrorCodes.Validation, "A portal path is required.").With("errors", new Dictionary<string, string[]> { ["path"] = ["invalid"] });
        }

        audit.Append("staff.access_denied", "PortalRoute", null, null, path.Length <= MaxPathLength ? path : path[..MaxPathLength]);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return TypedResults.NoContent();
    }
}
