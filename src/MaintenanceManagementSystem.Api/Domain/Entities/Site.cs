namespace MaintenanceManagementSystem.Api.Domain.Entities;

/// <summary>
/// A physical site belonging to exactly one organization.
/// All authenticated users can read sites of their organization;
/// only Approvers can create or update them.
/// </summary>
public class Site
{
    public int Id { get; set; }
    public int OrganizationId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }

    public Organization Organization { get; set; } = null!;
    public ICollection<MaintenanceRequest> MaintenanceRequests { get; set; } = new List<MaintenanceRequest>();
}
