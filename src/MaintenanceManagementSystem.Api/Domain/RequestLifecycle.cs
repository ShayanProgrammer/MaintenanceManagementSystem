using MaintenanceManagementSystem.Api.Domain.Entities;
using MaintenanceManagementSystem.Api.Domain.Enums;

namespace MaintenanceManagementSystem.Api.Domain;

/// <summary>
/// Explicit, code-enforced lifecycle transition map for maintenance requests.
/// Every status change must go through <see cref="ApplyTransition"/>; a
/// transition that is not listed here throws instead of silently corrupting
/// state.
///
/// Complete lifecycle (decisions 4/27/31 in DECISIONS.md):
///   Raised           -> PendingApproval (above threshold at creation/edit)
///                     | Approved       (at/below threshold, system)
///   PendingApproval  -> Approved (manual) | Rejected
///   Approved         -> Completed
/// Rejected and Completed are terminal.
/// </summary>
public static class RequestLifecycle
{
    private static readonly Dictionary<RequestStatus, IReadOnlySet<RequestStatus>> AllowedTransitions = new()
    {
        // Creation, or an edit that re-runs the threshold rule:
        // EstimatedCost <= threshold -> Approved (system auto-approval),
        // EstimatedCost >  threshold -> PendingApproval.
        [RequestStatus.Raised] = new HashSet<RequestStatus>
        {
            RequestStatus.PendingApproval,
            RequestStatus.Approved
        },

        // Manual approver decision, or a cost edit that drops to/below the
        // threshold (system auto-approval).
        [RequestStatus.PendingApproval] = new HashSet<RequestStatus>
        {
            RequestStatus.Approved,
            RequestStatus.Rejected
        },

        // The raiser records the actual cost and completes the work.
        [RequestStatus.Approved] = new HashSet<RequestStatus>
        {
            RequestStatus.Completed
        }

        // Rejected and Completed are terminal: no outgoing transitions.
    };

    public static bool CanTransition(RequestStatus from, RequestStatus to) =>
        AllowedTransitions.TryGetValue(from, out var targets) && targets.Contains(to);

    /// <summary>
    /// Applies a status change and returns the previous status (for the audit
    /// entry's PreviousStatus). Throws on any transition the lifecycle does
    /// not explicitly allow.
    /// </summary>
    public static RequestStatus ApplyTransition(MaintenanceRequest request, RequestStatus to)
    {
        var from = request.Status;

        if (!CanTransition(from, to))
        {
            throw new InvalidOperationException(
                $"Invalid status transition {from} -> {to} for maintenance request {request.Id}.");
        }

        request.Status = to;
        return from;
    }
}
