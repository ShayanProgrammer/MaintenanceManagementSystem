using MaintenanceManagementSystem.Api.Domain;
using MaintenanceManagementSystem.Api.Domain.Entities;
using MaintenanceManagementSystem.Api.Domain.Enums;
using Xunit;

namespace MaintenanceManagementSystem.Tests;

/// <summary>
/// Domain-level lifecycle hardening: the explicit transition map is the only
/// way a status changes, terminal states have no outgoing transitions, and
/// ApplyTransition refuses anything unmapped. The HTTP-level state guards
/// (409 responses for deciding/completing/editing in the wrong state) are
/// covered by the workflow integration tests; these tests pin the map itself.
/// </summary>
public class RequestLifecycleTests
{
    private static MaintenanceRequest RequestWithStatus(RequestStatus status) =>
        new() { Status = status };

    [Fact]
    public void Allowed_transitions_match_the_documented_lifecycle()
    {
        // Creation / edit re-evaluation.
        Assert.True(RequestLifecycle.CanTransition(RequestStatus.Raised, RequestStatus.PendingApproval));
        Assert.True(RequestLifecycle.CanTransition(RequestStatus.Raised, RequestStatus.Approved));

        // Manual approver decision / edit-triggered system auto-approval.
        Assert.True(RequestLifecycle.CanTransition(RequestStatus.PendingApproval, RequestStatus.Approved));
        Assert.True(RequestLifecycle.CanTransition(RequestStatus.PendingApproval, RequestStatus.Rejected));

        // Close-out by the raiser.
        Assert.True(RequestLifecycle.CanTransition(RequestStatus.Approved, RequestStatus.Completed));

        // Everything else is illegal.
        Assert.False(RequestLifecycle.CanTransition(RequestStatus.Raised, RequestStatus.Rejected));
        Assert.False(RequestLifecycle.CanTransition(RequestStatus.Raised, RequestStatus.Completed));
        Assert.False(RequestLifecycle.CanTransition(RequestStatus.PendingApproval, RequestStatus.Completed));
        Assert.False(RequestLifecycle.CanTransition(RequestStatus.Approved, RequestStatus.PendingApproval));
        Assert.False(RequestLifecycle.CanTransition(RequestStatus.Approved, RequestStatus.Rejected));
    }

    [Fact]
    public void Terminal_states_have_no_outgoing_transitions()
    {
        foreach (var terminal in new[] { RequestStatus.Rejected, RequestStatus.Completed })
        {
            foreach (var target in Enum.GetValues<RequestStatus>())
            {
                Assert.False(RequestLifecycle.CanTransition(terminal, target),
                    $"{terminal} must be terminal; a transition to {target} must not exist.");
            }
        }
    }

    [Fact]
    public void ApplyTransition_returns_previous_status_and_rejects_illegal_ones()
    {
        var request = RequestWithStatus(RequestStatus.PendingApproval);

        var previous = RequestLifecycle.ApplyTransition(request, RequestStatus.Rejected);
        Assert.Equal(RequestStatus.PendingApproval, previous);
        Assert.Equal(RequestStatus.Rejected, request.Status);

        // Terminal: any further change throws and leaves the status untouched.
        Assert.Throws<InvalidOperationException>(
            () => RequestLifecycle.ApplyTransition(request, RequestStatus.Approved));
        Assert.Equal(RequestStatus.Rejected, request.Status);
    }
}
