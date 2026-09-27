namespace MaintenanceManagementSystem.Api.Contracts;

// Read shape for audit records. Projected from AuditEntry in the service —
// EF entities are never returned directly. Actor fields are null for system
// decisions (e.g. AutoApproved): the null actor is the reserved marker for
// system-made decisions.

public record AuditEntryDto(
    int Id,
    int MaintenanceRequestId,
    int? ActorUserId,
    string? ActorEmail,
    string Action,
    string? PreviousStatus,
    string? NewStatus,
    string? Details,
    DateTime TimestampUtc);
