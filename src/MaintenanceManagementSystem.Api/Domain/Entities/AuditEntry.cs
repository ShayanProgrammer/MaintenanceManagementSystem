using MaintenanceManagementSystem.Api.Domain.Enums;

namespace MaintenanceManagementSystem.Api.Domain.Entities;

/// <summary>
/// Append-only audit record. Every state change and approval decision
/// (including system auto-approvals) produces exactly one entry, written in
/// the same transaction as the business change. The application exposes no
/// update or delete path for audit records.
/// </summary>
public class AuditEntry
{
    public int Id { get; set; }
    public int OrganizationId { get; set; }
    public int MaintenanceRequestId { get; set; }

    /// <summary>User who performed the action. For system auto-approval this
    /// is the user whose submission triggered the decision.</summary>
    public int? ActorUserId { get; set; }

    /// <summary>e.g. RequestRaised, AutoApproved, Approved, Rejected, Completed.</summary>
    public string Action { get; set; } = string.Empty;

    public RequestStatus? PreviousStatus { get; set; }
    public RequestStatus? NewStatus { get; set; }

    public string? Details { get; set; }
    public DateTime TimestampUtc { get; set; }

    public Organization Organization { get; set; } = null!;
    public MaintenanceRequest MaintenanceRequest { get; set; } = null!;
    public User? Actor { get; set; }
}
