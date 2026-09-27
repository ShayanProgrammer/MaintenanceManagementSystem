using System.ComponentModel.DataAnnotations;

namespace MaintenanceManagementSystem.Api.Contracts;

// Create/update request DTOs deliberately contain NO OrganizationId:
// the server assigns the tenant from the authenticated identity.

public record CreateSiteRequest(
    [Required, StringLength(200, MinimumLength = 1)] string Name,
    [StringLength(1000)] string? Description);

public record UpdateSiteRequest(
    [Required, StringLength(200, MinimumLength = 1)] string Name,
    [StringLength(1000)] string? Description);

public record SiteDto(int Id, int OrganizationId, string Name, string? Description);
