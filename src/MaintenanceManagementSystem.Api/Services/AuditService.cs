using MaintenanceManagementSystem.Api.Auth;
using MaintenanceManagementSystem.Api.Contracts;
using MaintenanceManagementSystem.Api.Data;
using MaintenanceManagementSystem.Api.Domain.Entities;
using MaintenanceManagementSystem.Api.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace MaintenanceManagementSystem.Api.Services;

/// <summary>
/// The single choke point for audit entries. Writes are staging only: entries
/// are attached to the caller's DbContext and persisted by the same
/// SaveChangesAsync that writes the business change, so audit rows commit
/// atomically with it. The only read is the tenant-scoped, chronological
/// listing behind GET /api/audit. There is no update or delete path — audit
/// is append-only.
/// </summary>
public class AuditService
{
    private readonly AppDbContext _db;
    private readonly TenantContext _tenant;

    public AuditService(AppDbContext db, TenantContext tenant)
    {
        _db = db;
        _tenant = tenant;
    }

    /// <summary>
    /// Stages one audit entry; it is saved when the caller next calls
    /// SaveChangesAsync. The entry hangs off the request via the navigation
    /// property so EF resolves MaintenanceRequestId for requests that do not
    /// have a database id yet, and the organization is copied from the
    /// request so the audit row can never be attached to another tenant.
    /// </summary>
    public void Stage(
        MaintenanceRequest maintenanceRequest,
        int? actorUserId,
        string action,
        RequestStatus? previousStatus,
        RequestStatus? newStatus,
        string? details,
        DateTime timestampUtc)
    {
        _db.AuditEntries.Add(new AuditEntry
        {
            OrganizationId = maintenanceRequest.OrganizationId,
            MaintenanceRequest = maintenanceRequest,
            ActorUserId = actorUserId,
            Action = action,
            PreviousStatus = previousStatus,
            NewStatus = newStatus,
            Details = details,
            TimestampUtc = timestampUtc
        });
    }

    /// <summary>
    /// Lists the caller's organization's audit entries in chronological
    /// order (timestamp, then id). Optionally narrowed to one maintenance
    /// request. Tenant isolation comes from the global query filter; the
    /// explicit organization predicate on the requestId path is defense in
    /// depth (decision 22). A foreign or unknown request id yields an empty
    /// list — indistinguishable, so existence is never revealed.
    /// </summary>
    public async Task<List<AuditEntryDto>> ListAsync(int? maintenanceRequestId)
    {
        var entries = _db.AuditEntries.AsNoTracking();

        if (maintenanceRequestId.HasValue)
        {
            entries = entries
                .Where(a => a.MaintenanceRequestId == maintenanceRequestId.Value
                            && a.OrganizationId == _tenant.RequireOrganizationId());
        }

        return await entries
            .OrderBy(a => a.TimestampUtc)
            .ThenBy(a => a.Id)
            .Select(a => new AuditEntryDto(
                a.Id,
                a.MaintenanceRequestId,
                a.ActorUserId,
                a.Actor!.Email,
                a.Action,
                a.PreviousStatus.ToString(),
                a.NewStatus.ToString(),
                a.Details,
                a.TimestampUtc))
            .ToListAsync();
    }
}
