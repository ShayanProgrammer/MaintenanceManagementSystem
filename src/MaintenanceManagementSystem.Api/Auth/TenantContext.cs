using System.Security.Claims;
using MaintenanceManagementSystem.Api.Domain.Enums;
using Microsoft.AspNetCore.Http;

namespace MaintenanceManagementSystem.Api.Auth;

/// <summary>
/// Server-side identity of the authenticated user, resolved once per request
/// from the validated JWT claims.
///
/// All tenant scoping in the application must be based on this context —
/// an organization id supplied by the client is never trusted. Later phases
/// build EF Core global query filters on top of this class.
/// </summary>
public class TenantContext
{
    public int UserId { get; }
    public int OrganizationId { get; }
    public UserRole Role { get; }

    public TenantContext(IHttpContextAccessor httpContextAccessor)
    {
        var user = httpContextAccessor.HttpContext?.User
            ?? throw new InvalidOperationException("TenantContext is only available within an HTTP request.");

        if (user.Identity?.IsAuthenticated != true)
        {
            throw new InvalidOperationException("TenantContext requires an authenticated user.");
        }

        UserId = ParseIntClaim(user, AppClaimTypes.UserId);
        OrganizationId = ParseIntClaim(user, AppClaimTypes.OrganizationId);
        Role = Enum.Parse<UserRole>(GetRequiredClaim(user, AppClaimTypes.Role));
    }

    private static string GetRequiredClaim(ClaimsPrincipal user, string claimType) =>
        user.FindFirstValue(claimType)
        ?? throw new InvalidOperationException($"Required claim '{claimType}' is missing from the token.");

    private static int ParseIntClaim(ClaimsPrincipal user, string claimType) =>
        int.TryParse(GetRequiredClaim(user, claimType), out var value)
            ? value
            : throw new InvalidOperationException($"Claim '{claimType}' is not a valid integer.");
}
