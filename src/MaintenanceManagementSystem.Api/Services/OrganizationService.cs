using MaintenanceManagementSystem.Api.Auth;
using MaintenanceManagementSystem.Api.Contracts;
using MaintenanceManagementSystem.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace MaintenanceManagementSystem.Api.Services;

/// <summary>
/// Organization operations for the authenticated user's own organization.
/// The organization is always determined by TenantContext — never by any
/// client-supplied id.
/// </summary>
public class OrganizationService
{
    private readonly AppDbContext _db;
    private readonly TenantContext _tenant;

    public OrganizationService(AppDbContext db, TenantContext tenant)
    {
        _db = db;
        _tenant = tenant;
    }

    /// <summary>Returns the caller's organization (the only one the global
    /// query filter can possibly return).</summary>
    public async Task<OrganizationDto> GetCurrentAsync()
    {
        var organizationId = _tenant.RequireOrganizationId();

        var organization = await _db.Organizations
            .SingleAsync(o => o.Id == organizationId);

        return new OrganizationDto(organization.Id, organization.Name, organization.ApprovalThreshold);
    }

    /// <summary>Approver-only (enforced by the endpoint policy). Updates the
    /// caller's own organization's approval threshold.</summary>
    public async Task<OrganizationDto> UpdateThresholdAsync(decimal approvalThreshold)
    {
        var organizationId = _tenant.RequireOrganizationId();

        var organization = await _db.Organizations
            .SingleAsync(o => o.Id == organizationId);

        organization.ApprovalThreshold = approvalThreshold;
        await _db.SaveChangesAsync();

        return new OrganizationDto(organization.Id, organization.Name, organization.ApprovalThreshold);
    }
}
