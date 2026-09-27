using System.Net;
using System.Net.Http.Json;
using MaintenanceManagementSystem.Api.Contracts;
using MaintenanceManagementSystem.Api.Domain.Enums;
using Xunit;

namespace MaintenanceManagementSystem.Tests;

/// <summary>
/// GET /api/audit: authenticated, tenant-scoped, chronological read access
/// to the audit trail. Data is produced exclusively through the public API
/// (create/approve/reject/complete), so no test code touches the database
/// at all — the endpoint's tenant scoping is verified through the API only.
/// Seeded identities (ids: alice=1, bob=2, carol=3, dave=4):
///   org 1 (Northgate Facilities, threshold 1000.00): alice Requester, bob Approver
///   org 2 (Summit Property Group,  threshold 2500.00): carol Requester, dave Approver
/// </summary>
public class AuditEndpointTests : IClassFixture<ApiFactory>
{
    private const string Alice = "alice.requester@northgate.example";
    private const string Bob = "bob.approver@northgate.example";
    private const string Carol = "carol.requester@summit.example";

    private const int AliceUserId = 1;
    private const int BobUserId = 2;

    private const int HqTowerSiteId = 1;

    private readonly ApiFactory _factory;
    private readonly HttpClient _anonymous;

    public AuditEndpointTests(ApiFactory factory)
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

    private async Task<HttpResponseMessage> DecideAsync(
        HttpClient client, int id, ApprovalDecision decision, string? reason) =>
        await client.PostAsJsonAsync($"/api/maintenance-requests/{id}/approvals",
            new CreateApprovalDecisionRequest(decision, reason));

    private async Task<List<AuditEntryDto>> AuditAsync(HttpClient client, int? requestId = null) =>
        await client.GetFromJsonAsync<List<AuditEntryDto>>(
            requestId.HasValue ? $"/api/audit?requestId={requestId.Value}" : "/api/audit") ?? [];

    // ------------------------------------------------------------------
    // Authorization
    // ------------------------------------------------------------------

    [Fact]
    public async Task Requester_can_read_own_organization_audit()
    {
        var alice = await ClientForAsync(Alice);
        await CreateAsync(alice, HqTowerSiteId, "Alice reads audit", 250m);

        var entries = await AuditAsync(alice);

        Assert.NotEmpty(entries);
        Assert.Contains(entries, e => e.Action == "RequestRaised" && e.ActorUserId == AliceUserId);
    }

    [Fact]
    public async Task Approver_can_read_own_organization_audit()
    {
        var bob = await ClientForAsync(Bob);
        await CreateAsync(bob, HqTowerSiteId, "Bob reads audit", 250m);

        var entries = await AuditAsync(bob);

        Assert.NotEmpty(entries);
    }

    [Fact]
    public async Task Anonymous_requests_are_rejected()
    {
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await _anonymous.GetAsync("/api/audit")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await _anonymous.GetAsync("/api/audit?requestId=1")).StatusCode);
    }

    // ------------------------------------------------------------------
    // Tenant isolation
    // ------------------------------------------------------------------

    [Fact]
    public async Task Audit_listing_returns_only_caller_organization_entries()
    {
        var alice = await ClientForAsync(Alice);
        var carol = await ClientForAsync(Carol);

        var aliceRequest = await CreateAsync(alice, HqTowerSiteId, "Org 1 audit entry", 250m);
        var carolRequest = await CreateAsync(carol, 3, "Org 2 audit entry", 2600m);

        var aliceEntries = await AuditAsync(alice);
        var carolEntries = await AuditAsync(carol);

        // Every row each caller sees belongs to one of their own requests.
        Assert.Contains(aliceEntries, e => e.MaintenanceRequestId == aliceRequest.Id);
        Assert.DoesNotContain(aliceEntries, e => e.MaintenanceRequestId == carolRequest.Id);
        Assert.Contains(carolEntries, e => e.MaintenanceRequestId == carolRequest.Id);
        Assert.DoesNotContain(carolEntries, e => e.MaintenanceRequestId == aliceRequest.Id);

        // The strong invariant: every audit row a caller sees maps to a
        // request visible through their own tenant-scoped request listing.
        var aliceRequestIds = (await alice.GetFromJsonAsync<List<MaintenanceRequestDto>>(
                "/api/maintenance-requests"))!
            .Select(r => r.Id).ToHashSet();
        Assert.All(aliceEntries, e => Assert.Contains(e.MaintenanceRequestId, aliceRequestIds));

        var carolRequestIds = (await carol.GetFromJsonAsync<List<MaintenanceRequestDto>>(
                "/api/maintenance-requests"))!
            .Select(r => r.Id).ToHashSet();
        Assert.All(carolEntries, e => Assert.Contains(e.MaintenanceRequestId, carolRequestIds));
    }

    [Fact]
    public async Task Cross_tenant_request_id_returns_empty_list()
    {
        var alice = await ClientForAsync(Alice);
        var foreign = await CreateAsync(alice, HqTowerSiteId, "Not Carol's request", 1500m);
        await DecideAsync(await ClientForAsync(Bob), foreign.Id, ApprovalDecision.Approve, null);
        Assert.Equal(3, (await AuditAsync(alice, foreign.Id)).Count); // sanity: audit exists in org 1

        var carol = await ClientForAsync(Carol);
        var entries = await AuditAsync(carol, foreign.Id);

        // 200 with no rows: a foreign id is indistinguishable from an
        // unknown one — existence is never revealed.
        Assert.Empty(entries);

        // An unknown id behaves identically.
        Assert.Empty(await AuditAsync(carol, 999999));
    }

    // ------------------------------------------------------------------
    // Filtering and ordering
    // ------------------------------------------------------------------

    [Fact]
    public async Task Request_id_filter_returns_only_that_request_history_in_order()
    {
        var alice = await ClientForAsync(Alice);
        var bob = await ClientForAsync(Bob);

        var pending = await CreateAsync(alice, HqTowerSiteId, "Full lifecycle", 1500m);
        var other = await CreateAsync(alice, HqTowerSiteId, "Unrelated request", 1500m);
        await DecideAsync(bob, pending.Id, ApprovalDecision.Approve, null);
        await alice.PostAsJsonAsync($"/api/maintenance-requests/{pending.Id}/completion",
            new CreateCompletionRequest(1600m));

        var entries = await AuditAsync(alice, pending.Id);

        Assert.Equal(4, entries.Count);
        Assert.All(entries, e => Assert.Equal(pending.Id, e.MaintenanceRequestId));
        Assert.Equal(
            new[] { "RequestRaised", "SubmittedForApproval", "Approved", "Completed" },
            entries.Select(e => e.Action).ToArray());

        // Deterministic chronological ordering: timestamp asc, then id asc.
        Assert.Equal(entries.OrderBy(e => e.TimestampUtc).ThenBy(e => e.Id).ToList(), entries);

        // The unrelated request's rows do not bleed in.
        var otherEntries = await AuditAsync(alice, other.Id);
        Assert.All(otherEntries, e => Assert.Equal(other.Id, e.MaintenanceRequestId));
    }

    // ------------------------------------------------------------------
    // Actor representation
    // ------------------------------------------------------------------

    [Fact]
    public async Task System_auto_approval_has_null_actor_fields()
    {
        var alice = await ClientForAsync(Alice);
        var auto = await CreateAsync(alice, HqTowerSiteId, "System decision", 250m);

        var entries = await AuditAsync(alice, auto.Id);
        var autoApproved = entries.Single(e => e.Action == "AutoApproved");

        Assert.Null(autoApproved.ActorUserId);
        Assert.Null(autoApproved.ActorEmail);
        Assert.Equal("Raised", autoApproved.PreviousStatus);
        Assert.Equal("Approved", autoApproved.NewStatus);
        Assert.Contains("System", autoApproved.Details, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Human_approval_entry_exposes_the_approver()
    {
        var alice = await ClientForAsync(Alice);
        var pending = await CreateAsync(alice, HqTowerSiteId, "Who approved?", 1500m);

        var bob = await ClientForAsync(Bob);
        await DecideAsync(bob, pending.Id, ApprovalDecision.Approve, null);

        var entries = await AuditAsync(alice, pending.Id);
        var approved = entries.Single(e => e.Action == "Approved");

        Assert.Equal(BobUserId, approved.ActorUserId);
        Assert.Equal(Bob, approved.ActorEmail);
        Assert.Equal("PendingApproval", approved.PreviousStatus);
        Assert.Equal("Approved", approved.NewStatus);
    }

    [Fact]
    public async Task Human_rejection_entry_exposes_approver_and_reason()
    {
        var alice = await ClientForAsync(Alice);
        var pending = await CreateAsync(alice, HqTowerSiteId, "Who rejected?", 1500m);

        var bob = await ClientForAsync(Bob);
        const string reason = "Over budget.";
        await DecideAsync(bob, pending.Id, ApprovalDecision.Reject, reason);

        var entries = await AuditAsync(alice, pending.Id);
        var rejected = entries.Single(e => e.Action == "Rejected");

        Assert.Equal(BobUserId, rejected.ActorUserId);
        Assert.Equal(Bob, rejected.ActorEmail);
        Assert.Equal("PendingApproval", rejected.PreviousStatus);
        Assert.Equal("Rejected", rejected.NewStatus);
        Assert.Contains(reason, rejected.Details);
    }

    [Fact]
    public async Task Completion_entry_exposes_the_raiser()
    {
        var alice = await ClientForAsync(Alice);
        var auto = await CreateAsync(alice, HqTowerSiteId, "Who completed?", 250m);

        await alice.PostAsJsonAsync($"/api/maintenance-requests/{auto.Id}/completion",
            new CreateCompletionRequest(255m));

        var entries = await AuditAsync(alice, auto.Id);
        var completed = entries.Single(e => e.Action == "Completed");

        Assert.Equal(AliceUserId, completed.ActorUserId);
        Assert.Equal(Alice, completed.ActorEmail);
        Assert.Equal("Approved", completed.PreviousStatus);
        Assert.Equal("Completed", completed.NewStatus);
        Assert.Contains("255", completed.Details);
    }

    // ------------------------------------------------------------------
    // Read-only surface
    // ------------------------------------------------------------------

    [Fact]
    public async Task Audit_has_no_write_or_delete_api()
    {
        var alice = await ClientForAsync(Alice);

        // No route matches these verbs/paths: the API surface is GET-only.
        Assert.Equal(HttpStatusCode.NotFound,
            (await alice.PostAsJsonAsync("/api/audit/1", new { action = "X" })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await alice.PutAsJsonAsync("/api/audit/1", new { action = "X" })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await alice.PatchAsJsonAsync("/api/audit/1", new { action = "X" })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await alice.DeleteAsync("/api/audit/1")).StatusCode);

        // POST to the collection route cannot create audit rows either:
        // the route only supports GET, so it is method-not-allowed.
        Assert.Equal(HttpStatusCode.MethodNotAllowed,
            (await alice.PostAsJsonAsync("/api/audit", new { action = "X" })).StatusCode);
    }
}
