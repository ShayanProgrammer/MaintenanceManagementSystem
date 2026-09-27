using MaintenanceManagementSystem.Api.Domain.Entities;
using MaintenanceManagementSystem.Api.Domain.Enums;

namespace MaintenanceManagementSystem.Api.Domain;

/// <summary>
/// Explicit, code-enforced lifecycle transition map for maintenance requests.
/// Every status change must go through <see cref="ApplyTransition"/>; a
/// transition that is not listed here throws instead of silently corrupting
/// state.
///
/// Phase 4 covers request creation only, so only the two creation-time
/// transitions are mapped. Later phases extend the map as manual
/// approval/rejection and completion are implemented.
/// </summary>
public static class RequestLifecycle
{
    private static readonly Dictionary<RequestStatus, IReadOnlySet<RequestStatus>> AllowedTransitions = new()
    {
        // Creation: EstimatedCost <= threshold -> Approved (auto),
        //           EstimatedCost >  threshold -> PendingApproval.
        [RequestStatus.Raised] = new HashSet<RequestStatus>
        {
            RequestStatus.PendingApproval,
            RequestStatus.Approved
        }

        // Later phases (from the agreed lifecycle):
        // PendingApproval -> Approved, PendingApproval -> Rejected,
        // Approved -> Completed.
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
