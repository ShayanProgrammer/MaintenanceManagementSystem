using System.Net;
using System.Net.Http.Json;
using MaintenanceManagementSystem.Api.Contracts;
using MaintenanceManagementSystem.Api.Data;
using MaintenanceManagementSystem.Api.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace MaintenanceManagementSystem.Tests;

/// <summary>
/// Phase 5 workflow: manual approval, rejection, completion, editing while
/// PendingApproval — authorization, lifecycle rules, tenant isolation, and
/// audit writing. Fresh in-memory database per test class.
/// Seeded identities (ids: alice=1, bob=2, carol=3, dave=4):
///   org 1 (Northgate Facilities, threshold 1000.00):
///     site 1 = HQ Tower, site 2 = Riverside Depot
///     alice.requester@northgate.example (Requester)
///     bob.approver@northgate.example    (Approver)
///   org 2 (Summit Property Group, threshold 2500.00):
///     site 3 = Summit Plaza
///     carol.requester@summit.example    (Requester)
///     dave.approver@summit.example      (Approver)
/// </summary>
public class MaintenanceRequestWorkflowTests : IClassFixture<ApiFactory>
{
    private const string Alice = "alice.requester@northgate.example";
    private const string Bob = "bob.approver@northgate.example";
    private const string Carol = "carol.requester@summit.example";
    private const string Dave = "dave.approver@summit.example";

    private const int AliceUserId = 1;
    private const int BobUserId = 2;

    private const int HqTowerSiteId = 1;
    private const decimal Org1Threshold = 1000.00m;

    private readonly ApiFactory _factory;
    private readonly HttpClient _anonymous;

    public MaintenanceRequestWorkflowTests(ApiFactory factory)
    {
        _factory = factory;
        _anonymous = factory.CreateClient();
    }

    private async Task<HttpClient> ClientForAsync(string email)
    {
        var login = await _anonymous.PostAsJsonAsync(
            "/api/auth/login", new LoginRequest(email, "Pass123$"));
        login.EnsureSuccessStatusCode();
        var token = (await login.Content.ReadFromJsonAsync<LoginResponse>())!.AccessToken;

        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", token);
        return client;
    }

    private async Task<MaintenanceRequestDto> CreateAsync(
        HttpClient client, int siteId, string title, decimal estimatedCost)
    {
        var response = await client.PostAsJsonAsync("/api/maintenance-requests",
            new CreateMaintenanceRequestRequest(siteId, title, "Created by test", estimatedCost));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<MaintenanceRequestDto>())!;
    }

    /// <summary>A PendingApproval request: org-1 raisers use a cost above
    /// their 1000 threshold.</summary>
    private Task<MaintenanceRequestDto> CreatePendingAsync(HttpClient client, string title) =>
        CreateAsync(client, HqTowerSiteId, title, 1500m);

    private Task<HttpResponseMessage> DecideAsync(
        HttpClient client, int id, ApprovalDecision decision, string? reason) =>
        client.PostAsJsonAsync($"/api/maintenance-requests/{id}/approvals",
            new CreateApprovalDecisionRequest(decision, reason));

    private Task<HttpResponseMessage> CompleteAsync(HttpClient client, int id, decimal actualCost) =>
        client.PostAsJsonAsync($"/api/maintenance-requests/{id}/completion",
            new CreateCompletionRequest(actualCost));

    private Task<HttpResponseMessage> EditAsync(
        HttpClient client, int id, int siteId, string title, decimal estimatedCost) =>
        client.PutAsJsonAsync($"/api/maintenance-requests/{id}",
            new UpdateMaintenanceRequestRequest(siteId, title, "Edited by test", estimatedCost));

    private async Task<MaintenanceRequestDto> GetRequestAsync(HttpClient client, int id)
    {
        var requests = await client.GetFromJsonAsync<List<MaintenanceRequestDto>>(
            "/api/maintenance-requests");
        return requests!.Single(r => r.Id == id);
    }

    /// <summary>
    /// Reads the audit trail of one request directly from the database. The
    /// scope has no HTTP context, so TenantContext is anonymous and the
    /// global query filter would match zero rows; IgnoreQueryFilters is safe
    /// here because the id comes from the caller's own created request.
    /// </summary>
    private async Task<List<Api.Domain.Entities.AuditEntry>> AuditTrailAsync(int maintenanceRequestId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        return await db.AuditEntries
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Where(a => a.MaintenanceRequestId == maintenanceRequestId)
            .OrderBy(a => a.Id)
            .ToListAsync();
    }

    // ------------------------------------------------------------------
    // Manual approval
    // ------------------------------------------------------------------

    [Fact]
    public async Task Approver_can_approve_another_users_pending_request()
    {
        var alice = await ClientForAsync(Alice);
        var pending = await CreatePendingAsync(alice, "Approve me");

        var bob = await ClientForAsync(Bob);
        var response = await DecideAsync(bob, pending.Id, ApprovalDecision.Approve, null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var approved = await response.Content.ReadFromJsonAsync<MaintenanceRequestDto>();
        Assert.Equal("Approved", approved!.Status);
        Assert.Equal(BobUserId, approved.ApprovedByUserId);   // human approver recorded
        Assert.NotNull(approved.ApprovedAtUtc);
    }

    [Fact]
    public async Task Requester_cannot_approve_even_own_pending_request()
    {
        var alice = await ClientForAsync(Alice);
        var pending = await CreatePendingAsync(alice, "Requester tries approve");

        var response = await DecideAsync(alice, pending.Id, ApprovalDecision.Approve, null);

        // 403 from the ApproverOnly endpoint policy, before any business rule.
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("PendingApproval", (await GetRequestAsync(alice, pending.Id)).Status);
    }

    [Fact]
    public async Task Approver_cannot_approve_own_request()
    {
        var bob = await ClientForAsync(Bob);
        var own = await CreatePendingAsync(bob, "Approver's own request");

        var response = await DecideAsync(bob, own.Id, ApprovalDecision.Approve, null);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("PendingApproval", (await GetRequestAsync(bob, own.Id)).Status);
    }

    [Fact]
    public async Task Cannot_approve_non_pending_request()
    {
        var alice = await ClientForAsync(Alice);
        var bob = await ClientForAsync(Bob);

        // Auto-approved at creation: never PendingApproval.
        var auto = await CreateAsync(alice, HqTowerSiteId, "Auto-approved", 250m);
        Assert.Equal(HttpStatusCode.Conflict,
            (await DecideAsync(bob, auto.Id, ApprovalDecision.Approve, null)).StatusCode);

        // Already manually approved.
        var pending = await CreatePendingAsync(alice, "Double decision");
        await DecideAsync(bob, pending.Id, ApprovalDecision.Approve, null);
        Assert.Equal(HttpStatusCode.Conflict,
            (await DecideAsync(bob, pending.Id, ApprovalDecision.Approve, null)).StatusCode);
    }

    [Fact]
    public async Task Cross_tenant_approver_cannot_approve()
    {
        var alice = await ClientForAsync(Alice);
        var pending = await CreatePendingAsync(alice, "Dave must not see this");

        var dave = await ClientForAsync(Dave);
        var response = await DecideAsync(dave, pending.Id, ApprovalDecision.Approve, null);

        // Foreign request id is indistinguishable from an unknown id: 404.
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("PendingApproval", (await GetRequestAsync(alice, pending.Id)).Status);
    }

    // ------------------------------------------------------------------
    // Rejection
    // ------------------------------------------------------------------

    [Fact]
    public async Task Approver_can_reject_another_users_pending_request_with_reason()
    {
        var alice = await ClientForAsync(Alice);
        var pending = await CreatePendingAsync(alice, "Reject me");

        var bob = await ClientForAsync(Bob);
        var response = await DecideAsync(bob, pending.Id, ApprovalDecision.Reject,
            "Estimated cost is too high for the current budget.");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var rejected = await response.Content.ReadFromJsonAsync<MaintenanceRequestDto>();
        Assert.Equal("Rejected", rejected!.Status);
        // The saved reason is verified via the audit trail in the dedicated
        // audit test; the DTO intentionally does not expose RejectionReason.
        Assert.Null(rejected.ApprovedByUserId);
        Assert.Null(rejected.ApprovedAtUtc);
    }

    [Fact]
    public async Task Rejection_without_reason_returns_400()
    {
        var alice = await ClientForAsync(Alice);
        var pending = await CreatePendingAsync(alice, "Reject without reason");

        var bob = await ClientForAsync(Bob);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await DecideAsync(bob, pending.Id, ApprovalDecision.Reject, null)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await DecideAsync(bob, pending.Id, ApprovalDecision.Reject, "   ")).StatusCode);

        // Untouched by the failed attempts.
        Assert.Equal("PendingApproval", (await GetRequestAsync(alice, pending.Id)).Status);
    }

    [Fact]
    public async Task Invalid_decision_value_returns_400()
    {
        var alice = await ClientForAsync(Alice);
        var pending = await CreatePendingAsync(alice, "Bad decision value");

        var bob = await ClientForAsync(Bob);
        var response = await bob.PostAsJsonAsync(
            $"/api/maintenance-requests/{pending.Id}/approvals",
            new { decision = "banana", reason = "x" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Requester_cannot_reject()
    {
        var alice = await ClientForAsync(Alice);
        var pending = await CreatePendingAsync(alice, "Requester tries reject");

        var response = await DecideAsync(
            alice, pending.Id, ApprovalDecision.Reject, "Nope");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Approver_cannot_reject_own_request()
    {
        var bob = await ClientForAsync(Bob);
        var own = await CreatePendingAsync(bob, "Approver rejects own");

        var response = await DecideAsync(bob, own.Id, ApprovalDecision.Reject, "Self reject");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("PendingApproval", (await GetRequestAsync(bob, own.Id)).Status);
    }

    [Fact]
    public async Task Cannot_reject_non_pending_request()
    {
        var alice = await ClientForAsync(Alice);
        var bob = await ClientForAsync(Bob);

        // Auto-approved at creation.
        var auto = await CreateAsync(alice, HqTowerSiteId, "Auto, then reject?", 300m);
        Assert.Equal(HttpStatusCode.Conflict,
            (await DecideAsync(bob, auto.Id, ApprovalDecision.Reject, "Too late")).StatusCode);

        // Already rejected.
        var pending = await CreatePendingAsync(alice, "Reject twice");
        await DecideAsync(bob, pending.Id, ApprovalDecision.Reject, "First rejection");
        Assert.Equal(HttpStatusCode.Conflict,
            (await DecideAsync(bob, pending.Id, ApprovalDecision.Reject, "Second rejection")).StatusCode);
    }

    // ------------------------------------------------------------------
    // Completion
    // ------------------------------------------------------------------

    [Fact]
    public async Task Raiser_can_complete_approved_request()
    {
        var alice = await ClientForAsync(Alice);
        var pending = await CreatePendingAsync(alice, "Complete me");

        var bob = await ClientForAsync(Bob);
        await DecideAsync(bob, pending.Id, ApprovalDecision.Approve, null);

        var response = await CompleteAsync(alice, pending.Id, 950m);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var completed = await response.Content.ReadFromJsonAsync<MaintenanceRequestDto>();
        Assert.Equal("Completed", completed!.Status);
        Assert.Equal(950m, completed.ActualCost);
        Assert.NotNull(completed.CompletedAtUtc);
    }

    [Fact]
    public async Task Raiser_can_complete_auto_approved_request()
    {
        var alice = await ClientForAsync(Alice);
        var auto = await CreateAsync(alice, HqTowerSiteId, "Auto-approved, completed", 250m);

        var response = await CompleteAsync(alice, auto.Id, 260m);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Completed", (await response.Content.ReadFromJsonAsync<MaintenanceRequestDto>())!.Status);
    }

    [Fact]
    public async Task Another_user_cannot_complete_request()
    {
        var alice = await ClientForAsync(Alice);
        var auto = await CreateAsync(alice, HqTowerSiteId, "Not Bob's job", 250m);

        // Bob is an Approver, but completion belongs to the raiser alone.
        var bob = await ClientForAsync(Bob);
        var response = await CompleteAsync(bob, auto.Id, 100m);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Cannot_complete_pending_or_rejected_request()
    {
        var alice = await ClientForAsync(Alice);
        var pending = await CreatePendingAsync(alice, "Not completeable yet");

        Assert.Equal(HttpStatusCode.Conflict,
            (await CompleteAsync(alice, pending.Id, 100m)).StatusCode);

        var bob = await ClientForAsync(Bob);
        await DecideAsync(bob, pending.Id, ApprovalDecision.Reject, "Won't happen");

        Assert.Equal(HttpStatusCode.Conflict,
            (await CompleteAsync(alice, pending.Id, 100m)).StatusCode);
    }

    [Fact]
    public async Task Cannot_complete_completed_request()
    {
        var alice = await ClientForAsync(Alice);
        var auto = await CreateAsync(alice, HqTowerSiteId, "Complete once", 250m);
        await CompleteAsync(alice, auto.Id, 250m);

        var response = await CompleteAsync(alice, auto.Id, 300m);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(250m, (await GetRequestAsync(alice, auto.Id)).ActualCost);
    }

    [Fact]
    public async Task Negative_actual_cost_returns_400()
    {
        var alice = await ClientForAsync(Alice);
        var auto = await CreateAsync(alice, HqTowerSiteId, "Negative actual", 250m);

        var response = await CompleteAsync(alice, auto.Id, -1m);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // ------------------------------------------------------------------
    // Editing while PendingApproval
    // ------------------------------------------------------------------

    [Fact]
    public async Task Raiser_can_edit_pending_request()
    {
        var alice = await ClientForAsync(Alice);
        var pending = await CreatePendingAsync(alice, "Original title");

        var response = await EditAsync(alice, pending.Id, HqTowerSiteId, "Edited title", 1600m);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var edited = await response.Content.ReadFromJsonAsync<MaintenanceRequestDto>();
        Assert.Equal("Edited title", edited!.Title);
        Assert.Equal(1600m, edited.EstimatedCost);
        Assert.Equal("PendingApproval", edited.Status);
    }

    [Fact]
    public async Task Approver_cannot_edit_another_users_request()
    {
        var alice = await ClientForAsync(Alice);
        var pending = await CreatePendingAsync(alice, "Alice's request");

        // Bob is an Approver — authorship, not role, grants editing.
        var bob = await ClientForAsync(Bob);
        var response = await EditAsync(bob, pending.Id, HqTowerSiteId, "Bob was here", 1500m);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("Alice's request", (await GetRequestAsync(alice, pending.Id)).Title);
    }

    [Fact]
    public async Task Cross_tenant_user_cannot_edit_request()
    {
        var alice = await ClientForAsync(Alice);
        var pending = await CreatePendingAsync(alice, "Still Alice's");

        var dave = await ClientForAsync(Dave);
        var response = await EditAsync(dave, pending.Id, HqTowerSiteId, "Dave was here", 1500m);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("Still Alice's", (await GetRequestAsync(alice, pending.Id)).Title);
    }

    [Fact]
    public async Task Cannot_edit_approved_rejected_or_completed_request()
    {
        var alice = await ClientForAsync(Alice);
        var bob = await ClientForAsync(Bob);

        var approved = await CreatePendingAsync(alice, "Approved, then edit?");
        await DecideAsync(bob, approved.Id, ApprovalDecision.Approve, null);
        Assert.Equal(HttpStatusCode.Conflict,
            (await EditAsync(alice, approved.Id, HqTowerSiteId, "X", 1500m)).StatusCode);

        var rejected = await CreatePendingAsync(alice, "Rejected, then edit?");
        await DecideAsync(bob, rejected.Id, ApprovalDecision.Reject, "No");
        Assert.Equal(HttpStatusCode.Conflict,
            (await EditAsync(alice, rejected.Id, HqTowerSiteId, "X", 1500m)).StatusCode);

        var completed = await CreateAsync(alice, HqTowerSiteId, "Completed, then edit?", 250m);
        await CompleteAsync(alice, completed.Id, 250m);
        Assert.Equal(HttpStatusCode.Conflict,
            (await EditAsync(alice, completed.Id, HqTowerSiteId, "X", 250m)).StatusCode);
    }

    [Fact]
    public async Task Cross_tenant_site_cannot_be_assigned_while_editing()
    {
        var alice = await ClientForAsync(Alice);
        var pending = await CreatePendingAsync(alice, "Site change attempt");

        // Site 3 (Summit Plaza) belongs to org 2: 404, same as unknown site.
        var response = await EditAsync(alice, pending.Id, 3, "Moved to Summit?", 1500m);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var unchanged = await GetRequestAsync(alice, pending.Id);
        Assert.Equal(HqTowerSiteId, unchanged.SiteId);
        Assert.Equal("Site change attempt", unchanged.Title);
    }

    [Fact]
    public async Task Editing_cost_to_at_or_below_threshold_auto_approves()
    {
        var alice = await ClientForAsync(Alice);
        var pending = await CreatePendingAsync(alice, "Cost reduced to boundary");

        // 1000.00 == threshold: boundary inclusive on edits too.
        var response = await EditAsync(alice, pending.Id, HqTowerSiteId, "Cost reduced", Org1Threshold);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var edited = await response.Content.ReadFromJsonAsync<MaintenanceRequestDto>();
        Assert.Equal("Approved", edited!.Status);
        Assert.NotNull(edited.ApprovedAtUtc);
        // System decision: no human approver may appear.
        Assert.Null(edited.ApprovedByUserId);
    }

    [Fact]
    public async Task Editing_cost_above_threshold_stays_pending()
    {
        var alice = await ClientForAsync(Alice);
        var pending = await CreatePendingAsync(alice, "Still expensive");

        var response = await EditAsync(alice, pending.Id, HqTowerSiteId, "Still expensive", 1200m);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("PendingApproval", (await response.Content.ReadFromJsonAsync<MaintenanceRequestDto>())!.Status);
    }

    // ------------------------------------------------------------------
    // Audit
    // ------------------------------------------------------------------

    [Fact]
    public async Task Manual_approval_creates_expected_audit_entries()
    {
        var alice = await ClientForAsync(Alice);
        var pending = await CreatePendingAsync(alice, "Audit: approval");

        var bob = await ClientForAsync(Bob);
        await DecideAsync(bob, pending.Id, ApprovalDecision.Approve, null);

        var entries = await AuditTrailAsync(pending.Id);
        Assert.Equal(3, entries.Count);

        Assert.Equal("RequestRaised", entries[0].Action);
        Assert.Equal(AliceUserId, entries[0].ActorUserId);

        Assert.Equal("SubmittedForApproval", entries[1].Action);
        Assert.Equal(AliceUserId, entries[1].ActorUserId);

        var approved = entries[2];
        Assert.Equal("Approved", approved.Action);
        Assert.Equal(BobUserId, approved.ActorUserId);            // human approver
        Assert.Equal(RequestStatus.PendingApproval, approved.PreviousStatus);
        Assert.Equal(RequestStatus.Approved, approved.NewStatus);
        Assert.False(string.IsNullOrWhiteSpace(approved.Details));
    }

    [Fact]
    public async Task Manual_rejection_creates_expected_audit_entries_with_reason()
    {
        var alice = await ClientForAsync(Alice);
        var pending = await CreatePendingAsync(alice, "Audit: rejection");

        var bob = await ClientForAsync(Bob);
        const string reason = "Budget exhausted this quarter.";
        await DecideAsync(bob, pending.Id, ApprovalDecision.Reject, reason);

        var entries = await AuditTrailAsync(pending.Id);
        Assert.Equal(3, entries.Count);

        var rejected = entries[2];
        Assert.Equal("Rejected", rejected.Action);
        Assert.Equal(BobUserId, rejected.ActorUserId);
        Assert.Equal(RequestStatus.PendingApproval, rejected.PreviousStatus);
        Assert.Equal(RequestStatus.Rejected, rejected.NewStatus);
        Assert.Contains(reason, rejected.Details);                 // reason preserved
    }

    [Fact]
    public async Task Completion_creates_expected_audit_entry()
    {
        var alice = await ClientForAsync(Alice);
        var auto = await CreateAsync(alice, HqTowerSiteId, "Audit: completion", 250m);
        await CompleteAsync(alice, auto.Id, 275m);

        var entries = await AuditTrailAsync(auto.Id);
        Assert.Equal(3, entries.Count);

        var completed = entries[2];
        Assert.Equal("Completed", completed.Action);
        Assert.Equal(AliceUserId, completed.ActorUserId);          // the raiser
        Assert.Equal(RequestStatus.Approved, completed.PreviousStatus);
        Assert.Equal(RequestStatus.Completed, completed.NewStatus);
        Assert.Contains("275", completed.Details);                 // actual cost
    }

    [Fact]
    public async Task Edit_caused_auto_approval_has_system_audit_entry()
    {
        var alice = await ClientForAsync(Alice);
        var pending = await CreatePendingAsync(alice, "Audit: edit auto-approval");

        await EditAsync(alice, pending.Id, HqTowerSiteId, "Edited", Org1Threshold);

        var entries = await AuditTrailAsync(pending.Id);
        Assert.Equal(3, entries.Count); // raised, submitted, auto-approved — nothing else

        var auto = entries[2];
        Assert.Equal("AutoApproved", auto.Action);
        Assert.Null(auto.ActorUserId);                             // system decision
        Assert.Equal(RequestStatus.PendingApproval, auto.PreviousStatus);
        Assert.Equal(RequestStatus.Approved, auto.NewStatus);
        Assert.Contains("edit", auto.Details, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("1000", auto.Details);                     // threshold named
    }

    [Fact]
    public async Task Audit_entries_have_no_modification_endpoints()
    {
        var alice = await ClientForAsync(Alice);

        foreach (var url in new[] { "/api/audit/1", "/api/auditentries/1" })
        {
            Assert.Equal(HttpStatusCode.NotFound,
                await alice.GetStatusAsync(url));
            Assert.Equal(HttpStatusCode.NotFound,
                (await alice.PutAsJsonAsync(url, new { action = "X" })).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound,
                (await alice.DeleteAsync(url)).StatusCode);
        }
    }

    [Fact]
    public async Task Foreign_tenant_attempts_write_no_audit_rows()
    {
        var alice = await ClientForAsync(Alice);
        var pending = await CreatePendingAsync(alice, "Audit: isolation");

        var dave = await ClientForAsync(Dave);
        Assert.Equal(HttpStatusCode.NotFound,
            (await DecideAsync(dave, pending.Id, ApprovalDecision.Approve, null)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await DecideAsync(dave, pending.Id, ApprovalDecision.Reject, "x")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await EditAsync(dave, pending.Id, HqTowerSiteId, "X", 1500m)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await CompleteAsync(dave, pending.Id, 100m)).StatusCode);

        // Only the two creation-time entries exist; nothing from the foreign caller.
        var entries = await AuditTrailAsync(pending.Id);
        Assert.Equal(2, entries.Count);
        Assert.All(entries, e => Assert.Equal(AliceUserId, e.ActorUserId));
    }
}
