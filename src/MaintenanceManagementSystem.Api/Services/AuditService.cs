using MaintenanceManagementSystem.Api.Data;
using MaintenanceManagementSystem.Api.Domain.Entities;
using MaintenanceManagementSystem.Api.Domain.Enums;

namespace MaintenanceManagementSystem.Api.Services;

/// <summary>
/// The single choke point for writing audit entries. Staging only: entries
/// are attached to the caller's DbContext and persisted by the same
/// SaveChangesAsync that writes the business change, so audit rows commit
/// atomically with it. There is deliberately no read, update, or delete
/// path — audit is append-only.
/// </summary>
public class AuditService
{
    private readonly AppDbContext _db;

    public AuditService(AppDbContext db)
    {
        _db = db;
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
}
