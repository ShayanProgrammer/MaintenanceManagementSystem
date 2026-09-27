using MaintenanceManagementSystem.Api.Domain.Enums;

namespace MaintenanceManagementSystem.Api.Domain.Entities;

/// <summary>
/// A user belonging to exactly one organization with exactly one role.
/// </summary>
public class User
{
    public int Id { get; set; }
    public int OrganizationId { get; set; }
    public string Email { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public UserRole Role { get; set; }

    public Organization Organization { get; set; } = null!;

    /// <summary>Requests this user raised.</summary>
    public ICollection<MaintenanceRequest> RaisedRequests { get; set; } = new List<MaintenanceRequest>();

    /// <summary>Requests this user approved or rejected (never their own).</summary>
    public ICollection<MaintenanceRequest> DecidedRequests { get; set; } = new List<MaintenanceRequest>();

    /// <summary>Audit entries recorded with this user as the actor.</summary>
    public ICollection<AuditEntry> AuditEntries { get; set; } = new List<AuditEntry>();
}
