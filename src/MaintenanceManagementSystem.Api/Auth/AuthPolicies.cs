namespace MaintenanceManagementSystem.Api.Auth;

/// <summary>Named authorization policies. Enforced server-side via
/// [Authorize(Policy = ...)] on approver-only endpoints from phase 3 on.</summary>
public static class AuthPolicies
{
    public const string ApproverOnly = "ApproverOnly";
}
