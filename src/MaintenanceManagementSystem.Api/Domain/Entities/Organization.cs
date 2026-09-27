namespace MaintenanceManagementSystem.Api.Domain.Entities;

/// <summary>
/// A tenant. All tenant-owned entities carry an OrganizationId so that
/// tenant isolation can be enforced with global query filters.
/// </summary>
public class Organization
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Requests with EstimatedCost &lt;= this value are approved
    /// automatically on submission; higher costs require an Approver.
    /// Configured per organization; changes are approver-only and audited.
    /// </summary>
    public decimal ApprovalThreshold { get; set; }

    public ICollection<Site> Sites { get; set; } = new List<Site>();
    public ICollection<User> Users { get; set; } = new List<User>();
    public ICollection<MaintenanceRequest> MaintenanceRequests { get; set; } = new List<MaintenanceRequest>();
    public ICollection<AuditEntry> AuditEntries { get; set; } = new List<AuditEntry>();
}
