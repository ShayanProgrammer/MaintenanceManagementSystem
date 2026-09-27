using MaintenanceManagementSystem.Api.Auth;
using MaintenanceManagementSystem.Api.Contracts;
using MaintenanceManagementSystem.Api.Domain.Enums;
using MaintenanceManagementSystem.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MaintenanceManagementSystem.Api.Controllers;

[ApiController]
[Route("api/maintenance-requests")]
public class MaintenanceRequestsController : ControllerBase
{
    private readonly MaintenanceRequestService _maintenanceRequestService;

    public MaintenanceRequestsController(MaintenanceRequestService maintenanceRequestService)
    {
        _maintenanceRequestService = maintenanceRequestService;
    }

    /// <summary>Lists the authenticated user's organization's maintenance
    /// requests. Available to any authenticated user (Requester or Approver).</summary>
    [HttpGet]
    [ProducesResponseType(typeof(List<MaintenanceRequestDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<List<MaintenanceRequestDto>>> List()
    {
        return Ok(await _maintenanceRequestService.ListAsync());
    }

    /// <summary>Any authenticated user may raise a request. The server
    /// assigns organization, raiser, timestamps and status; the approval
    /// threshold decides Raised -> Approved (system auto-approval) or
    /// Raised -> PendingApproval, and each outcome is audited in the same
    /// transaction. A site id of another organization returns 404 — exactly
    /// like an unknown id.</summary>
    [HttpPost]
    [ProducesResponseType(typeof(MaintenanceRequestDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<MaintenanceRequestDto>> Create(CreateMaintenanceRequestRequest request)
    {
        var created = await _maintenanceRequestService.CreateAsync(request);
        return created is null
            ? NotFound()
            : Created($"/api/maintenance-requests/{created.Id}", created);
    }

    /// <summary>Approver-only. Approves or rejects a PendingApproval request
    /// of the caller's organization. The approver must not be the raiser
    /// (403) and the request must be PendingApproval (409 — already-decided
    /// or auto-approved requests cannot be decided again). Rejection requires
    /// a reason (400). Another organization's request id returns 404 —
    /// exactly like an unknown id.</summary>
    [HttpPost("{id:int}/approvals")]
    [Authorize(Policy = AuthPolicies.ApproverOnly)]
    [ProducesResponseType(typeof(MaintenanceRequestDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<MaintenanceRequestDto>> Decide(
        int id, CreateApprovalDecisionRequest request)
    {
        // Conditional requirement the DTO attributes cannot express: reject
        // needs a reason at the API boundary.
        if (request.Decision == ApprovalDecision.Reject
            && string.IsNullOrWhiteSpace(request.Reason))
        {
            return BadRequest();
        }

        var result = await _maintenanceRequestService.DecideAsync(
            id, request.Decision!.Value, request.Reason);
        return FromResult(result);
    }

    /// <summary>Completes an Approved request. Only the user who raised the
    /// request may complete it (403 for anyone else — role is irrelevant);
    /// only Approved requests can be completed (409 otherwise). The actual
    /// cost never triggers another approval workflow. A foreign or unknown
    /// request id returns 404.</summary>
    [HttpPost("{id:int}/completion")]
    [ProducesResponseType(typeof(MaintenanceRequestDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<MaintenanceRequestDto>> Complete(
        int id, CreateCompletionRequest request)
    {
        var result = await _maintenanceRequestService.CompleteAsync(id, request);
        return FromResult(result);
    }

    /// <summary>Edits a request. Only the raiser may edit (an Approver has
    /// no special rights on someone else's request, 403), and only while it
    /// is PendingApproval (409 once Approved/Rejected/Completed). If the
    /// estimated cost changes, the organization's current threshold is
    /// re-evaluated: at or below it the request is auto-approved as a system
    /// decision (ApprovedByUserId stays null), above it the request stays
    /// PendingApproval. A foreign or unknown site id returns 404.</summary>
    [HttpPut("{id:int}")]
    [ProducesResponseType(typeof(MaintenanceRequestDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<MaintenanceRequestDto>> Update(
        int id, UpdateMaintenanceRequestRequest request)
    {
        var result = await _maintenanceRequestService.UpdateAsync(id, request);
        return FromResult(result);
    }

    private ActionResult FromResult(RequestMutationResult result) =>
        result.Status switch
        {
            RequestMutationStatus.Ok => Ok(result.Request!),
            RequestMutationStatus.NotFound => NotFound(),
            RequestMutationStatus.Forbidden => StatusCode(StatusCodes.Status403Forbidden),
            _ => Conflict()
        };
}
