using MaintenanceManagementSystem.Api.Contracts;
using MaintenanceManagementSystem.Api.Services;
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
}
