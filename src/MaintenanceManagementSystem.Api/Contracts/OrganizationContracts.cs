using System.ComponentModel.DataAnnotations;

namespace MaintenanceManagementSystem.Api.Contracts;

/// <summary>The authenticated user's own organization. The id always comes
/// from the server-side tenant identity, never from the client.</summary>
public record OrganizationDto(int Id, string Name, decimal ApprovalThreshold);

public record UpdateThresholdRequest(
    [Required, Range(0, 999_999_999_999.99)]
    decimal? ApprovalThreshold);
