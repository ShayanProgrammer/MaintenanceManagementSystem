using MaintenanceManagementSystem.Api.Auth;
using MaintenanceManagementSystem.Api.Contracts;
using MaintenanceManagementSystem.Api.Data;
using MaintenanceManagementSystem.Api.Domain;
using MaintenanceManagementSystem.Api.Domain.Entities;
using MaintenanceManagementSystem.Api.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace MaintenanceManagementSystem.Api.Services;

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

        return await Project(_db.MaintenanceRequests
                .Where(r => r.Id == maintenanceRequest.Id))
            .SingleAsync();
    }

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
