using System.ComponentModel.DataAnnotations;

namespace MaintenanceManagementSystem.Api.Contracts;

// The create DTO contains none of the server-assigned fields (OrganizationId,
// RaisedByUserId, Status, timestamps): the server assigns them from the
// authenticated identity and the threshold rule, so tenancy and workflow
// state cannot be smuggled in through request JSON.

public record CreateMaintenanceRequestRequest(
    [Required, Range(1, int.MaxValue)] int? SiteId,
    [Required, StringLength(200, MinimumLength = 1)] string Title,
    [Required, StringLength(2000, MinimumLength = 1)] string Description,
    [Required, Range(0, 999_999_999_999.99)] decimal? EstimatedCost);

public record MaintenanceRequestDto(
    int Id,
    int SiteId,
    string SiteName,
    string Title,
    string? Description,
    decimal EstimatedCost,
    decimal? ActualCost,
    string Status,
    int RaisedByUserId,
    string RaisedByEmail,
    DateTime CreatedAtUtc,
    DateTime? ApprovedAtUtc,
    int? ApprovedByUserId,
    DateTime? CompletedAtUtc);
