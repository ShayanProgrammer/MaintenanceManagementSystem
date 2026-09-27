using System.ComponentModel.DataAnnotations;
using MaintenanceManagementSystem.Api.Validation;

namespace MaintenanceManagementSystem.Api.Contracts;

// Create/update request DTOs deliberately contain NO OrganizationId:
// the server assigns the tenant from the authenticated identity.
// [NotWhitespace] rejects whitespace-only names: the service trims,
// so "   " would otherwise be stored as an empty site name.

public record CreateSiteRequest(
    [Required, StringLength(200, MinimumLength = 1), NotWhitespace] string Name,
    [StringLength(1000)] string? Description);

public record UpdateSiteRequest(
    [Required, StringLength(200, MinimumLength = 1), NotWhitespace] string Name,
    [StringLength(1000)] string? Description);

public record SiteDto(int Id, int OrganizationId, string Name, string? Description);
