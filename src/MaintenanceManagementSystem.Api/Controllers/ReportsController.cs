using MaintenanceManagementSystem.Api.Contracts;
using MaintenanceManagementSystem.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace MaintenanceManagementSystem.Api.Controllers;

/// <summary>
/// Read-only, organization-scoped reports. Authentication comes from the
/// fallback policy (Requester and Approver alike); no organizationId is ever
/// accepted from the client — the report always aggregates the caller's own
/// organization.
/// </summary>
[ApiController]
[Route("api/reports")]
public class ReportsController : ControllerBase
{
    private readonly ReportService _reportService;

    public ReportsController(ReportService reportService)
    {
        _reportService = reportService;
    }

    /// <summary>Total actual spend per site for maintenance requests
    /// completed within the inclusive date range. Dates are date-only and
    /// interpreted as UTC; a completion at any time on the "to" date is
    /// included. Invalid or inverted ranges return 400 — dates are never
    /// silently swapped.</summary>
    [HttpGet("spend")]
    [ProducesResponseType(typeof(List<SpendBySiteDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<List<SpendBySiteDto>>> Spend(
        [FromQuery] DateOnly? from, [FromQuery] DateOnly? to)
    {
        if (from is null || to is null)
        {
            return BadRequest();
        }

        if (from.Value > to.Value)
        {
            return BadRequest();
        }

        return Ok(await _reportService.SpendBySiteAsync(from.Value, to.Value));
    }
}
