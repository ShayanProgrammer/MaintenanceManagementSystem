using MaintenanceManagementSystem.Api.Auth;
using MaintenanceManagementSystem.Api.Contracts;
using MaintenanceManagementSystem.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MaintenanceManagementSystem.Api.Controllers;

[ApiController]
[Route("api/sites")]
public class SitesController : ControllerBase
{
    private readonly SiteService _siteService;

    public SitesController(SiteService siteService)
    {
        _siteService = siteService;
    }

    /// <summary>Lists the authenticated user's organization's sites.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(List<SiteDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<List<SiteDto>>> List()
    {
        return Ok(await _siteService.ListAsync());
    }

    /// <summary>Approver-only. Creates a site under the authenticated
    /// user's organization. The request DTO has no OrganizationId — the
    /// tenant is assigned server-side.</summary>
    [HttpPost]
    [Authorize(Policy = AuthPolicies.ApproverOnly)]
    [ProducesResponseType(typeof(SiteDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<SiteDto>> Create(CreateSiteRequest request)
    {
        var site = await _siteService.CreateAsync(request);
        return Created($"/api/sites/{site.Id}", site);
    }

    /// <summary>Approver-only. Updates a site of the authenticated user's
    /// organization. Another organization's site id returns 404 — exactly
    /// like an unknown id — so site existence in other tenants is never
    /// revealed.</summary>
    [HttpPut("{id:int}")]
    [Authorize(Policy = AuthPolicies.ApproverOnly)]
    [ProducesResponseType(typeof(SiteDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<SiteDto>> Update(int id, UpdateSiteRequest request)
    {
        var site = await _siteService.UpdateAsync(id, request);
        return site is null ? NotFound() : Ok(site);
    }
}
