using MaintenanceManagementSystem.Api.Auth;
using MaintenanceManagementSystem.Api.Contracts;
using MaintenanceManagementSystem.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MaintenanceManagementSystem.Api.Controllers;

/// <summary>
/// There is deliberately no /api/organizations/{id} route: an organization
/// is only ever addressed as "current", resolved from the authenticated
/// identity. Cross-organization access by URL manipulation has no target.
/// </summary>
[ApiController]
[Route("api/organizations")]
public class OrganizationsController : ControllerBase
{
    private readonly OrganizationService _organizationService;

    public OrganizationsController(OrganizationService organizationService)
    {
        _organizationService = organizationService;
    }

    /// <summary>Returns the authenticated user's own organization.</summary>
    [HttpGet("current")]
    [ProducesResponseType(typeof(OrganizationDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<OrganizationDto>> GetCurrent()
    {
        return Ok(await _organizationService.GetCurrentAsync());
    }

    /// <summary>Approver-only. Updates the approval threshold of the
    /// authenticated user's own organization.</summary>
    [HttpPut("current/threshold")]
    [Authorize(Policy = AuthPolicies.ApproverOnly)]
    [ProducesResponseType(typeof(OrganizationDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<OrganizationDto>> UpdateThreshold(UpdateThresholdRequest request)
    {
        return Ok(await _organizationService.UpdateThresholdAsync(request.ApprovalThreshold!.Value));
    }
}
