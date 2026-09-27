using MaintenanceManagementSystem.Api.Auth;
using MaintenanceManagementSystem.Api.Contracts;
using MaintenanceManagementSystem.Api.Data;
using MaintenanceManagementSystem.Api.Domain;
using MaintenanceManagementSystem.Api.Domain.Entities;
using MaintenanceManagementSystem.Api.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace MaintenanceManagementSystem.Api.Services;

/// <summary>Outcome of a workflow mutation (approve/reject/complete/edit).
/// Foreign or unknown ids are indistinguishable (NotFound); a same-tenant
/// caller without permission is Forbidden; a valid request in a state that
/// does not allow the operation is InvalidState.</summary>
public enum RequestMutationStatus
{
    Ok,
    NotFound,
    Forbidden,
    InvalidState
}

/// <summary>Result of a workflow mutation; <see cref="Request"/> is the
/// updated request when <see cref="RequestMutationStatus"/> is Ok.</summary>
public record RequestMutationResult(RequestMutationStatus Status, MaintenanceRequestDto? Request = null)
{
    public static readonly RequestMutationResult NotFound = new(RequestMutationStatus.NotFound);
    public static readonly RequestMutationResult Forbidden = new(RequestMutationStatus.Forbidden);
    public static readonly RequestMutationResult InvalidState = new(RequestMutationStatus.InvalidState);
}

/// <summary>
/// Creation and listing of maintenance requests, scoped to the authenticated
/// user's organization. The approval threshold rule is evaluated at creation:
/// EstimatedCost at or below the organization's threshold is auto-approved
/// (as a system decision, recorded in audit); above it the request enters
/// PendingApproval.
/// </summary>
public class MaintenanceRequestService
{
    private readonly AppDbContext _db;
    private readonly TenantContext _tenant;
    private readonly AuditService _audit;

    public MaintenanceRequestService(AppDbContext db, TenantContext tenant, AuditService audit)
    {
        _db = db;
        _tenant = tenant;
        _audit = audit;
    }

    /// <summary>Only the caller's organization's requests (global query
    /// filter), with site name and raiser email for readability.</summary>
    public async Task<List<MaintenanceRequestDto>> ListAsync()
    {
        return await Project(_db.MaintenanceRequests).ToListAsync();
    }

    /// <summary>
    /// Creates a request for the caller's organization. All tenancy and
    /// system fields are assigned here, never from the request body. Returns
    /// null when the site is unknown or belongs to another organization —
    /// both are 404.
    /// </summary>
    public async Task<MaintenanceRequestDto?> CreateAsync(CreateMaintenanceRequestRequest request)
    {
        var organizationId = _tenant.RequireOrganizationId();
        var userId = _tenant.RequireUserId();
        var timestampUtc = DateTime.UtcNow;

        // Tenant-scoped lookup (plus the global query filter): a foreign or
        // unknown site id is indistinguishable — both 404.
        var site = await _db.Sites.SingleOrDefaultAsync(s =>
            s.Id == request.SiteId!.Value
            && s.OrganizationId == organizationId);
        if (site is null)
        {
            return null;
        }

        // The threshold decision uses the organization's current value from
        // the database, not anything supplied by the client.
        var organization = await _db.Organizations.SingleAsync(o => o.Id == organizationId);

        var maintenanceRequest = new MaintenanceRequest
        {
            OrganizationId = organizationId,
            SiteId = site.Id,
            RaisedByUserId = userId,
            Title = request.Title.Trim(),
            Description = request.Description!.Trim(),
            EstimatedCost = request.EstimatedCost!.Value,
            Status = RequestStatus.Raised,
            CreatedAtUtc = timestampUtc
        };

        _db.MaintenanceRequests.Add(maintenanceRequest);

        // Audit 1: the creation itself (null -> Raised), by the creator.
        _audit.Stage(
            maintenanceRequest,
            actorUserId: userId,
            action: AuditActions.RequestRaised,
            previousStatus: null,
            newStatus: RequestStatus.Raised,
            details: $"Estimated cost {maintenanceRequest.EstimatedCost:0.00}; organization threshold {organization.ApprovalThreshold:0.00}.",
            timestampUtc);

        if (maintenanceRequest.EstimatedCost <= organization.ApprovalThreshold)
        {
            // Boundary inclusive: a cost exactly equal to the threshold is
            // auto-approved (agreed rule).
            var previousStatus = RequestLifecycle.ApplyTransition(
                maintenanceRequest, RequestStatus.Approved);

            // No human decided this: ApprovedByUserId stays null, and the
            // audit actor is null to mark the decision as system-made.
            maintenanceRequest.ApprovedAtUtc = timestampUtc;

            _audit.Stage(
                maintenanceRequest,
                actorUserId: null,
                action: AuditActions.AutoApproved,
                previousStatus: previousStatus,
                newStatus: RequestStatus.Approved,
                details: $"System auto-approved: estimated cost {maintenanceRequest.EstimatedCost:0.00} is at or below the organization threshold {organization.ApprovalThreshold:0.00}.",
                timestampUtc);
        }
        else
        {
            var previousStatus = RequestLifecycle.ApplyTransition(
                maintenanceRequest, RequestStatus.PendingApproval);

            _audit.Stage(
                maintenanceRequest,
                actorUserId: userId,
                action: AuditActions.SubmittedForApproval,
                previousStatus: previousStatus,
                newStatus: RequestStatus.PendingApproval,
                details: $"Estimated cost {maintenanceRequest.EstimatedCost:0.00} exceeds the organization threshold {organization.ApprovalThreshold:0.00}.",
                timestampUtc);
        }

        // One SaveChangesAsync commits the request and both audit rows
        // atomically; the navigation property supplies the request id FK.
        await _db.SaveChangesAsync();

        return await ProjectByIdAsync(maintenanceRequest.Id);
    }

    /// <summary>
    /// Applies a manual approver decision (approve or reject) to a
    /// PendingApproval request of the caller's organization. The endpoint
    /// policy already guarantees the Approver role; the service enforces the
    /// resource rules: same organization (via the tenant-scoped lookup — a
    /// foreign id is NotFound), never the raiser's own request, and only
    /// from PendingApproval.
    /// </summary>
    public async Task<RequestMutationResult> DecideAsync(int id, ApprovalDecision decision, string? reason)
    {
        var organizationId = _tenant.RequireOrganizationId();
        var userId = _tenant.RequireUserId();
        var now = DateTime.UtcNow;

        var maintenanceRequest = await _db.MaintenanceRequests.SingleOrDefaultAsync(r =>
            r.Id == id
            && r.OrganizationId == organizationId);
        if (maintenanceRequest is null)
        {
            return RequestMutationResult.NotFound; // unknown OR another tenant's
        }

        // An approver may never decide their own request.
        if (maintenanceRequest.RaisedByUserId == userId)
        {
            return RequestMutationResult.Forbidden;
        }

        // Only a request that is actually awaiting approval can be decided;
        // auto-approved or already-decided requests are rejected here
        // (409 at the endpoint).
        if (maintenanceRequest.Status != RequestStatus.PendingApproval)
        {
            return RequestMutationResult.InvalidState;
        }

        switch (decision)
        {
            case ApprovalDecision.Approve:
            {
                var previousStatus = RequestLifecycle.ApplyTransition(
                    maintenanceRequest, RequestStatus.Approved);

                maintenanceRequest.ApprovedByUserId = userId;
                maintenanceRequest.ApprovedAtUtc = now;
                maintenanceRequest.RejectionReason = null;

                _audit.Stage(
                    maintenanceRequest,
                    actorUserId: userId,
                    action: AuditActions.Approved,
                    previousStatus: previousStatus,
                    newStatus: RequestStatus.Approved,
                    details: "Manual approval by an organization approver.",
                    now);
                break;
            }
            case ApprovalDecision.Reject:
            {
                // The boundary already enforces a non-blank reason; the trim
                // guards against whitespace-only values.
                var trimmedReason = reason!.Trim();

                var previousStatus = RequestLifecycle.ApplyTransition(
                    maintenanceRequest, RequestStatus.Rejected);

                // ApprovedByUserId/ApprovedAtUtc remain null on rejection;
                // RejectionReason records why.
                maintenanceRequest.RejectionReason = trimmedReason;

                _audit.Stage(
                    maintenanceRequest,
                    actorUserId: userId,
                    action: AuditActions.Rejected,
                    previousStatus: previousStatus,
                    newStatus: RequestStatus.Rejected,
                    details: $"Rejection reason: {trimmedReason}",
                    now);
                break;
            }
            default:
                return RequestMutationResult.InvalidState;
        }

        await _db.SaveChangesAsync();
        return new RequestMutationResult(
            RequestMutationStatus.Ok, await ProjectByIdAsync(maintenanceRequest.Id));
    }

    /// <summary>
    /// Completes an Approved request. Only the original raiser may complete
    /// it — the role is irrelevant; only authorship of the request counts.
    /// The actual cost never re-runs the threshold rule (decision 8).
    /// </summary>
    public async Task<RequestMutationResult> CompleteAsync(int id, CreateCompletionRequest request)
    {
        var organizationId = _tenant.RequireOrganizationId();
        var userId = _tenant.RequireUserId();
        var now = DateTime.UtcNow;

        var maintenanceRequest = await _db.MaintenanceRequests.SingleOrDefaultAsync(r =>
            r.Id == id
            && r.OrganizationId == organizationId);
        if (maintenanceRequest is null)
        {
            return RequestMutationResult.NotFound; // unknown OR another tenant's
        }

        if (maintenanceRequest.RaisedByUserId != userId)
        {
            return RequestMutationResult.Forbidden; // only the raiser completes
        }

        if (maintenanceRequest.Status != RequestStatus.Approved)
        {
            return RequestMutationResult.InvalidState; // Pending/Rejected/Completed
        }

        var previousStatus = RequestLifecycle.ApplyTransition(
            maintenanceRequest, RequestStatus.Completed);

        maintenanceRequest.ActualCost = request.ActualCost!.Value;
        maintenanceRequest.CompletedAtUtc = now;

        _audit.Stage(
            maintenanceRequest,
            actorUserId: userId,
            action: AuditActions.Completed,
            previousStatus: previousStatus,
            newStatus: RequestStatus.Completed,
            details: $"Completed with actual cost {maintenanceRequest.ActualCost:0.00} (estimated {maintenanceRequest.EstimatedCost:0.00}).",
            now);

        await _db.SaveChangesAsync();
        return new RequestMutationResult(
            RequestMutationStatus.Ok, await ProjectByIdAsync(maintenanceRequest.Id));
    }

    /// <summary>
    /// Edits a PendingApproval request. Only the raiser may edit, and only
    /// while the request is PendingApproval. If the estimated cost changes,
    /// the organization's current threshold is re-evaluated: at or below the
    /// threshold the request is auto-approved as a system decision
    /// (ApprovedByUserId stays null, system audit entry); above it the
    /// request remains PendingApproval with no audit noise. The edit can
    /// never move a request to Rejected or Completed — the lifecycle map
    /// simply has no such transition from PendingApproval.
    /// </summary>
    public async Task<RequestMutationResult> UpdateAsync(int id, UpdateMaintenanceRequestRequest request)
    {
        var organizationId = _tenant.RequireOrganizationId();
        var userId = _tenant.RequireUserId();
        var now = DateTime.UtcNow;

        var maintenanceRequest = await _db.MaintenanceRequests.SingleOrDefaultAsync(r =>
            r.Id == id
            && r.OrganizationId == organizationId);
        if (maintenanceRequest is null)
        {
            return RequestMutationResult.NotFound; // unknown OR another tenant's
        }

        // Authorship, not role: an Approver has no special edit rights on
        // someone else's request.
        if (maintenanceRequest.RaisedByUserId != userId)
        {
            return RequestMutationResult.Forbidden;
        }

        if (maintenanceRequest.Status != RequestStatus.PendingApproval)
        {
            return RequestMutationResult.InvalidState; // Approved/Rejected/Completed
        }

        // The new site must belong to the caller's organization; a foreign
        // or unknown site id is indistinguishable — both 404.
        var site = await _db.Sites.SingleOrDefaultAsync(s =>
            s.Id == request.SiteId!.Value
            && s.OrganizationId == organizationId);
        if (site is null)
        {
            return RequestMutationResult.NotFound;
        }

        var costChanged = maintenanceRequest.EstimatedCost != request.EstimatedCost!.Value;

        maintenanceRequest.SiteId = site.Id;
        maintenanceRequest.Title = request.Title.Trim();
        maintenanceRequest.Description = request.Description!.Trim();
        maintenanceRequest.EstimatedCost = request.EstimatedCost.Value;

        if (costChanged)
        {
            // Re-evaluate the organization's current threshold (which may
            // itself have changed since creation).
            var organization = await _db.Organizations.SingleAsync(o => o.Id == organizationId);

            if (maintenanceRequest.EstimatedCost <= organization.ApprovalThreshold)
            {
                var previousStatus = RequestLifecycle.ApplyTransition(
                    maintenanceRequest, RequestStatus.Approved);

                // System decision: no human approver, null audit actor.
                maintenanceRequest.ApprovedAtUtc = now;

                _audit.Stage(
                    maintenanceRequest,
                    actorUserId: null,
                    action: AuditActions.AutoApproved,
                    previousStatus: previousStatus,
                    newStatus: RequestStatus.Approved,
                    details: $"System auto-approved after edit: estimated cost {maintenanceRequest.EstimatedCost:0.00} is at or below the organization threshold {organization.ApprovalThreshold:0.00}.",
                    now);
            }
            // Above the threshold: remains PendingApproval. No audit entry —
            // only state changes and approval decisions are audited.
        }

        await _db.SaveChangesAsync();
        return new RequestMutationResult(
            RequestMutationStatus.Ok, await ProjectByIdAsync(maintenanceRequest.Id));
    }

    private async Task<MaintenanceRequestDto> ProjectByIdAsync(int id) =>
        await Project(_db.MaintenanceRequests
                .Where(r => r.Id == id))
            .SingleAsync();

    /// <summary>Shared read shape for list and create responses.</summary>
    private static IQueryable<MaintenanceRequestDto> Project(IQueryable<MaintenanceRequest> requests) =>
        requests
            .OrderBy(r => r.Id)
            .Select(r => new MaintenanceRequestDto(
                r.Id,
                r.SiteId,
                r.Site.Name,
                r.Title,
                r.Description,
                r.EstimatedCost,
                r.ActualCost,
                r.Status.ToString(),
                r.RaisedByUserId,
                r.RaisedBy.Email,
                r.CreatedAtUtc,
                r.ApprovedAtUtc,
                r.ApprovedByUserId,
                r.CompletedAtUtc));
}
