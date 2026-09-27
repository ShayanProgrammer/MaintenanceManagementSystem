namespace MaintenanceManagementSystem.Api.Auth;

/// <summary>
/// JWT configuration. In Development these values come from user-secrets;
/// in other environments from environment variables / configuration.
/// The signing key must never be committed to source control.
/// </summary>
public class JwtSettings
{
    public const string SectionName = "Jwt";

    public string Issuer { get; set; } = string.Empty;
    public string Audience { get; set; } = string.Empty;
    public string SigningKey { get; set; } = string.Empty;

    /// <summary>Access token lifetime in minutes. Development default: 60.
    /// No refresh tokens in v1 (see DECISIONS.md).</summary>
    public int LifetimeMinutes { get; set; } = 60;
}
