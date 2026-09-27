namespace MaintenanceManagementSystem.Api.Domain;

/// <summary>
/// Constants for AuditEntry.Action values. Plain string constants (the
/// column is a string) so audit rows stay readable in SQL and immune to enum
/// renumbering. Audit is append-only: never rename an action that may
/// already exist in the database — add a new constant instead.
/// </summary>
public static class AuditActions
{
    /// <summary>Request created (null -> Raised). Actor is the creator.</summary>
    public const string RequestRaised = "RequestRaised";

    /// <summary>System auto-approval at creation (Raised -> Approved)
    /// because EstimatedCost is at or below the organization's approval
    /// threshold. ActorUserId is null — no human made this decision.</summary>
    public const string AutoApproved = "AutoApproved";

    /// <summary>Request entered the approval queue at creation
    /// (Raised -> PendingApproval) because EstimatedCost exceeds the
    /// threshold. Actor is the creator.</summary>
    public const string SubmittedForApproval = "SubmittedForApproval";

    /// <summary>Manual approval by an organization approver
    /// (PendingApproval -> Approved). Actor is the approver.</summary>
    public const string Approved = "Approved";

    /// <summary>Manual rejection by an organization approver
    /// (PendingApproval -> Rejected). Actor is the approver; details
    /// contain the required rejection reason.</summary>
    public const string Rejected = "Rejected";

    /// <summary>Completion by the original raiser (Approved ->
    /// Completed). Actor is the raiser; details contain the actual
    /// cost.</summary>
    public const string Completed = "Completed";
}
