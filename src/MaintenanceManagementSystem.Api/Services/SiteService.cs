using MaintenanceManagementSystem.Api.Auth;
using MaintenanceManagementSystem.Api.Contracts;
using MaintenanceManagementSystem.Api.Data;
using MaintenanceManagementSystem.Api.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace MaintenanceManagementSystem.Api.Services;

/// <summary>
/// Site management scoped to the authenticated user's organization.
/// Listing is available to any organization member; create/update are
/// approver-only (enforced by endpoint policies).
/// </summary>
public class SiteService
{
    private readonly AppDbContext _db;
    private readonly TenantContext _tenant;

    public SiteService(AppDbContext db, TenantContext tenant)
    {
        _db = db;
        _tenant = tenant;
    }

    /// <summary>Only the caller's organization's sites (global query filter).</summary>
    public async Task<List<SiteDto>> ListAsync()
    {
        return await _db.Sites
            .OrderBy(s => s.Id)
            .Select(s => new SiteDto(s.Id, s.OrganizationId, s.Name, s.Description))
            .ToListAsync();
    }

    /// <summary>Creates a site under the caller's own organization. The
    /// tenant comes from the authenticated identity — never the request.</summary>
    public async Task<SiteDto> CreateAsync(CreateSiteRequest request)
    {
        var site = new Site
        {
            OrganizationId = _tenant.RequireOrganizationId(),
            Name = request.Name.Trim(),
            Description = request.Description
        };

        _db.Sites.Add(site);
        await _db.SaveChangesAsync();

        return new SiteDto(site.Id, site.OrganizationId, site.Name, site.Description);
    }

    /// <summary>Updates a site of the caller's own organization. The lookup
    /// is tenant-scoped (plus the global query filter), so another
    /// organization's site id behaves exactly like an unknown id: 404.</summary>
    public async Task<SiteDto?> UpdateAsync(int id, UpdateSiteRequest request)
    {
        var organizationId = _tenant.RequireOrganizationId();

        var site = await _db.Sites.SingleOrDefaultAsync(s =>
            s.Id == id
            && s.OrganizationId == organizationId);

        if (site is null)
        {
            return null; // unknown OR owned by another tenant — both 404
        }

        site.Name = request.Name.Trim();
        site.Description = request.Description;
        await _db.SaveChangesAsync();

        return new SiteDto(site.Id, site.OrganizationId, site.Name, site.Description);
    }
}
