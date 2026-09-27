namespace MaintenanceManagementSystem.Api.Domain.Enums;

/// <summary>
/// A user holds exactly one role. Approvers may also raise maintenance
/// requests, but only Approvers can approve or reject requests, and never
/// their own.
/// </summary>
public enum UserRole
{
    Requester = 1,
    Approver = 2
}
