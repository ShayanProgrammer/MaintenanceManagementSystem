using System.Text.Json.Serialization;

namespace MaintenanceManagementSystem.Api.Domain.Enums;

/// <summary>
/// A human approval decision on a PendingApproval request. Bound from JSON
/// as a case-insensitive string ("approve" / "reject") via
/// JsonStringEnumConverter; an unrecognized value fails model binding with
/// 400 at the API boundary.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ApprovalDecision
{
    Approve = 1,
    Reject = 2
}
