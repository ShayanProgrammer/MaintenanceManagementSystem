namespace MaintenanceManagementSystem.Api.Auth;

/// <summary>
/// Claim type names written into the JWT. Inbound mapping is disabled
/// (MapInboundClaims = false) so the claim names are identical on both
/// sides of the token.
/// </summary>
public static class AppClaimTypes
{
    /// <summary>Standard JWT subject claim: the user id.</summary>
    public const string UserId = "sub";
    public const string OrganizationId = "org";
    public const string Role = "role";
    public const string Email = "email";
}
