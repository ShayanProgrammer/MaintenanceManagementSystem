using MaintenanceManagementSystem.Api.Contracts;
using MaintenanceManagementSystem.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace MaintenanceManagementSystem.Api.Controllers;

/// <summary>
/// Read-only, authenticated access to the caller's organization's audit
/// trail. The fallback authorization policy requires an authenticated user
/// (Requester and Approver alike); there is deliberately no POST/PUT/PATCH/
/// DELETE surface — audit stays application-write-only.
/// </summary>
[ApiController]
[Route("api/audit")]
public class AuditController : ControllerBase
{
    private readonly AuditService _auditService;

    public AuditController(AuditService auditService)
    {
        _auditService = auditService;
    }

    /// <summary>Lists the authenticated user's organization's audit entries
    /// in chronological order. With <c>requestId</c>, only that request's
    /// history — provided it belongs to the caller's organization; a foreign
    /// or unknown id returns an empty list, never an error or foreign rows.
    /// OrganizationId is never accepted from the client.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(List<AuditEntryDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<List<AuditEntryDto>>> List([FromQuery] int? requestId)
    {
        return Ok(await _auditService.ListAsync(requestId));
    }
}
