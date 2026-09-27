using System.Security.Claims;
using MaintenanceManagementSystem.Api.Domain.Enums;
using Microsoft.AspNetCore.Http;

namespace MaintenanceManagementSystem.Api.Auth;

/// <summary>
/// Server-side identity of the authenticated user, resolved once per request
/// from the validated JWT claims.
///
/// Outside an authenticated HTTP request (seeding, background work, anonymous
/// endpoints) the identity properties are null. That makes tenant query
/// filters "default deny": a missing identity can match zero rows, but never
/// all tenants. Code that requires an identity calls the Require* methods,
/// which fail loudly instead of silently proceeding without a tenant.
///
/// All tenant scoping in the application must be based on this context —
/// an organization id supplied by the client is never trusted.
/// </summary>
public class TenantContext
{
    public int? UserId { get; }
    public int? OrganizationId { get; }
    public UserRole? Role { get; }

    public bool IsAuthenticated => UserId.HasValue;

    public TenantContext(IHttpContextAccessor httpContextAccessor)
    {
        var user = httpContextAccessor.HttpContext?.User;
        if (user?.Identity?.IsAuthenticated != true)
        {
            return; // no authenticated request context — identity stays null
        }

        UserId = ParseIntClaim(user, AppClaimTypes.UserId);
        OrganizationId = ParseIntClaim(user, AppClaimTypes.OrganizationId);
        Role = Enum.Parse<UserRole>(GetRequiredClaim(user, AppClaimTypes.Role));
    }

    public int RequireUserId() =>
        UserId ?? throw new InvalidOperationException("An authenticated user is required for this operation.");

    public int RequireOrganizationId() =>
        OrganizationId ?? throw new InvalidOperationException("An authenticated organization context is required for this operation.");

    public UserRole RequireRole() =>
        Role ?? throw new InvalidOperationException("An authenticated role is required for this operation.");

    private static string GetRequiredClaim(ClaimsPrincipal user, string claimType) =>
        user.FindFirstValue(claimType)
        ?? throw new InvalidOperationException($"Required claim '{claimType}' is missing from the token.");

    private static int ParseIntClaim(ClaimsPrincipal user, string claimType) =>
        int.TryParse(GetRequiredClaim(user, claimType), out var value)
            ? value
            : throw new InvalidOperationException($"Claim '{claimType}' is not a valid integer.");
}
