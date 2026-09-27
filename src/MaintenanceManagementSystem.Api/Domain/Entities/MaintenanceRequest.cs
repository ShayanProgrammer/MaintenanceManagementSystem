using MaintenanceManagementSystem.Api.Domain.Enums;

namespace MaintenanceManagementSystem.Api.Domain.Entities;

/// <summary>
/// A maintenance request raised against a site.
/// OrganizationId is denormalized (it is also reachable via Site) so that
/// tenant isolation is enforced directly on this table without joins and
/// cannot be bypassed through the Site navigation.
/// </summary>
public class MaintenanceRequest
{
    public int Id { get; set; }
    public int OrganizationId { get; set; }
    public int SiteId { get; set; }
    public int RaisedByUserId { get; set; }

    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }

    /// <summary>
    /// Drives the approval threshold rule. Editable by the raiser only while
    /// the request is PendingApproval; each change re-runs the threshold rule.
    /// </summary>
    public decimal EstimatedCost { get; set; }

    /// <summary>
    /// Recorded by the raiser when the request is Completed. Does not trigger
    /// a second approval workflow in v1 (documented in DECISIONS.md).
    /// </summary>
    public decimal? ActualCost { get; set; }

    public RequestStatus Status { get; set; } = RequestStatus.Raised;

    /// <summary>Approver who approved or rejected the request (never the raiser).</summary>
    public int? ApprovedByUserId { get; set; }

    /// <summary>Required when a request is rejected.</summary>
    public string? RejectionReason { get; set; }

    public DateTime? ApprovedAtUtc { get; set; }
    public DateTime? CompletedAtUtc { get; set; }
    public DateTime CreatedAtUtc { get; set; }

    public Organization Organization { get; set; } = null!;
    public Site Site { get; set; } = null!;
    public User RaisedBy { get; set; } = null!;
    public User? ApprovedBy { get; set; }
    public ICollection<AuditEntry> AuditEntries { get; set; } = new List<AuditEntry>();
}
