using System.ComponentModel.DataAnnotations;
using MaintenanceManagementSystem.Api.Domain.Enums;
using MaintenanceManagementSystem.Api.Validation;

namespace MaintenanceManagementSystem.Api.Contracts;

// The create DTO contains none of the server-assigned fields (OrganizationId,
// RaisedByUserId, Status, timestamps): the server assigns them from the
// authenticated identity and the threshold rule, so tenancy and workflow
// state cannot be smuggled in through request JSON.
// [NotWhitespace] rejects whitespace-only strings: the service trims
// titles/descriptions, so "   " would otherwise be stored as an empty value.

public record CreateMaintenanceRequestRequest(
    [Required, Range(1, int.MaxValue)] int? SiteId,
    [Required, StringLength(200, MinimumLength = 1), NotWhitespace] string Title,
    [Required, StringLength(2000, MinimumLength = 1), NotWhitespace] string Description,
    [Required, Range(0, 999_999_999_999.99)] decimal? EstimatedCost);

// The edit DTO has exactly the same shape and validation as the create DTO.
// All workflow-owned fields (Status, ActualCost, ApprovedByUserId, timestamps,
// RejectionReason, OrganizationId, RaisedByUserId) are absent by construction:
// they remain server-controlled.

public record UpdateMaintenanceRequestRequest(
    [Required, Range(1, int.MaxValue)] int? SiteId,
    [Required, StringLength(200, MinimumLength = 1), NotWhitespace] string Title,
    [Required, StringLength(2000, MinimumLength = 1), NotWhitespace] string Description,
    [Required, Range(0, 999_999_999_999.99)] decimal? EstimatedCost);

// Approval/rejection share one endpoint and one DTO. Decision binds as a
// case-insensitive string ("approve"/"reject"); anything else is a 400 at
// the boundary. Reason is required for Reject (checked at the boundary and
// re-enforced in the service); max length matches the entity's
// RejectionReason column (1000).

public record CreateApprovalDecisionRequest(
    [Required] ApprovalDecision? Decision,
    [StringLength(1000)] string? Reason);

// Completion records the actual cost. It never triggers a second approval
// workflow, whatever its value (decision 8).

public record CreateCompletionRequest(
    [Required, Range(0, 999_999_999_999.99)] decimal? ActualCost);

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
