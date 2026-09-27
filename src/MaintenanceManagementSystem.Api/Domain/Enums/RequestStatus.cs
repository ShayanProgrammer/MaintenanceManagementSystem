namespace MaintenanceManagementSystem.Api.Domain.Enums;

/// <summary>
/// Lifecycle of a maintenance request:
/// Raised -> PendingApproval -> Approved / Rejected -> Completed.
/// There is intentionally no Draft state; the approval threshold is
/// evaluated at creation (and at cost edits while PendingApproval).
/// Rejected and Completed are terminal states.
/// </summary>
public enum RequestStatus
{
    Raised = 1,
    PendingApproval = 2,
    Approved = 3,
    Rejected = 4,
    Completed = 5
}
