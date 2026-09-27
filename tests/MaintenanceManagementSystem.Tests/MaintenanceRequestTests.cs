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
/// Request creation, threshold auto-approval, org-scoped listing, and audit
/// writing for /api/maintenance-requests.
/// Seeded identities (fresh in-memory database per test class):
///   org 1 (Northgate Facilities, threshold 1000.00):
///     site 1 = HQ Tower, site 2 = Riverside Depot
///     alice.requester@northgate.example (Requester)
///     bob.approver@northgate.example    (Approver)
///   org 2 (Summit Property Group, threshold 2500.00):
///     site 3 = Summit Plaza
///     carol.requester@summit.example    (Requester)
/// </summary>
public class MaintenanceRequestTests : IClassFixture<ApiFactory>
{
    private const string Alice = "alice.requester@northgate.example";
    private const string Bob = "bob.approver@northgate.example";
    private const string Carol = "carol.requester@summit.example";

    private const int HqTowerSiteId = 1;
    private const decimal Org1Threshold = 1000.00m;

    private readonly ApiFactory _factory;
    private readonly HttpClient _anonymous;

    public MaintenanceRequestTests(ApiFactory factory)
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
    // Creation and the threshold rule
    // ------------------------------------------------------------------

    [Fact]
    public async Task Requester_create_below_threshold_is_auto_approved()
    {
        var alice = await ClientForAsync(Alice);

        var created = await CreateAsync(alice, HqTowerSiteId, "Leaking faucet", 250m);

        Assert.True(created.Id > 0);
        Assert.Equal("HQ Tower", created.SiteName);
        Assert.Equal(Alice, created.RaisedByEmail);
        Assert.Equal("Approved", created.Status);

        // Auto-approval: decision time recorded, no human approver attached.
        Assert.NotNull(created.ApprovedAtUtc);
        Assert.Null(created.ApprovedByUserId);
        Assert.Null(created.ActualCost);
        Assert.Null(created.CompletedAtUtc);
    }

    [Fact]
    public async Task Cost_exactly_at_threshold_is_auto_approved()
    {
        // Boundary is inclusive per the agreed rule: 1000.00 <= 1000.00.
        var alice = await ClientForAsync(Alice);

        var created = await CreateAsync(
            alice, HqTowerSiteId, "Boundary test", Org1Threshold);

        Assert.Equal("Approved", created.Status);
        Assert.NotNull(created.ApprovedAtUtc);
        Assert.Null(created.ApprovedByUserId);
    }

    [Fact]
    public async Task Cost_above_threshold_goes_to_pending_approval()
    {
        var alice = await ClientForAsync(Alice);

        var created = await CreateAsync(
            alice, HqTowerSiteId, "Roof repair", Org1Threshold + 0.01m);

        Assert.Equal("PendingApproval", created.Status);
        Assert.Null(created.ApprovedAtUtc);
        Assert.Null(created.ApprovedByUserId);
    }

    [Fact]
    public async Task Approver_can_also_create_and_auto_approval_still_applies()
    {
        var bob = await ClientForAsync(Bob);

        var created = await CreateAsync(bob, HqTowerSiteId, "Door closer fix", 300m);

        Assert.Equal("Approved", created.Status);
        Assert.Equal(Bob, created.RaisedByEmail);
        // Even though Bob is an Approver, he did not approve this request —
        // the system did. ApprovedByUserId must still be null.
        Assert.Null(created.ApprovedByUserId);
    }

    [Fact]
    public async Task Zero_cost_is_valid_and_auto_approved()
    {
        var alice = await ClientForAsync(Alice);

        var created = await CreateAsync(alice, HqTowerSiteId, "Zero cost inspection", 0m);

        Assert.Equal("Approved", created.Status);
    }

    [Fact]
    public async Task Negative_estimated_cost_is_rejected()
    {
        var alice = await ClientForAsync(Alice);

        var response = await alice.PostAsJsonAsync("/api/maintenance-requests",
            new CreateMaintenanceRequestRequest(HqTowerSiteId, "Bad cost", "Created by test", -0.01m));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Invalid_model_state_is_rejected()
    {
        var alice = await ClientForAsync(Alice);

        // Empty title violates [Required/StringLength]; SiteId 0 violates Range(1,..).
        var emptyTitle = await alice.PostAsJsonAsync("/api/maintenance-requests",
            new CreateMaintenanceRequestRequest(HqTowerSiteId, "", "Created by test", 10m));
        Assert.Equal(HttpStatusCode.BadRequest, emptyTitle.StatusCode);

        var siteIdZero = await alice.PostAsJsonAsync("/api/maintenance-requests",
            new CreateMaintenanceRequestRequest(0, "No site", "Created by test", 10m));
        Assert.Equal(HttpStatusCode.BadRequest, siteIdZero.StatusCode);

        var missingCost = await alice.PostAsJsonAsync("/api/maintenance-requests",
            new { SiteId = HqTowerSiteId, Title = "No cost", Description = "Created by test" });
        Assert.Equal(HttpStatusCode.BadRequest, missingCost.StatusCode);
    }

    // ------------------------------------------------------------------
    // Site ownership and tenant isolation
    // ------------------------------------------------------------------

    [Fact]
    public async Task Org_1_user_cannot_create_request_against_org_2_site()
    {
        var alice = await ClientForAsync(Alice);

        // Site 3 (Summit Plaza) belongs to org 2 — indistinguishable from an
        // unknown site: 404, never 403.
        var response = await alice.PostAsJsonAsync("/api/maintenance-requests",
            new CreateMaintenanceRequestRequest(3, "Cross-tenant probe", "Created by test", 50m));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

        // Nothing was created in the other tenant.
        var carol = await ClientForAsync(Carol);
        var org2Requests = await carol.GetFromJsonAsync<List<MaintenanceRequestDto>>(
            "/api/maintenance-requests");
        Assert.DoesNotContain(org2Requests!, r => r.Title == "Cross-tenant probe");
    }

    [Fact]
    public async Task Unknown_site_returns_404()
    {
        var alice = await ClientForAsync(Alice);

        var response = await alice.PostAsJsonAsync("/api/maintenance-requests",
            new CreateMaintenanceRequestRequest(9999, "Ghost site", "Created by test", 50m));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Listing_is_org_scoped()
    {
        var alice = await ClientForAsync(Alice);
        var probe = await CreateAsync(alice, HqTowerSiteId, "Isolation probe", 25m);

        var org1Requests = await alice.GetFromJsonAsync<List<MaintenanceRequestDto>>(
            "/api/maintenance-requests");
        Assert.NotNull(org1Requests);
        Assert.Contains(org1Requests!, r => r.Id == probe.Id);
        // Order-independent invariants: every row belongs to org 1 (its site
        // is an org-1 site and the raiser is an org-1 user).
        Assert.All(org1Requests!, r =>
            Assert.Contains(r.SiteName, new[] { "HQ Tower", "Riverside Depot" }));
        Assert.All(org1Requests!, r =>
            Assert.Contains(r.RaisedByEmail, new[] { Alice, Bob }));

        var carol = await ClientForAsync(Carol);
        var org2Requests = await carol.GetFromJsonAsync<List<MaintenanceRequestDto>>(
            "/api/maintenance-requests");
        Assert.NotNull(org2Requests);
        Assert.DoesNotContain(org2Requests!, r => r.Id == probe.Id);
        Assert.All(org2Requests!, r => Assert.Equal("Summit Plaza", r.SiteName));
    }

    // ------------------------------------------------------------------
    // Audit trail
    // ------------------------------------------------------------------

    [Fact]
    public async Task Auto_approval_writes_RequestRaised_then_system_AutoApproved_audit()
    {
        var alice = await ClientForAsync(Alice);
        var created = await CreateAsync(alice, HqTowerSiteId, "Audit: auto-approved", 250m);

        var entries = await AuditTrailAsync(created.Id);

        Assert.Equal(2, entries.Count);

        var raised = entries[0];
        Assert.Equal("RequestRaised", raised.Action);
        Assert.Null(raised.PreviousStatus);
        Assert.Equal(RequestStatus.Raised, raised.NewStatus);
        Assert.Equal(created.RaisedByUserId, raised.ActorUserId); // the creator
        Assert.False(string.IsNullOrWhiteSpace(raised.Details));

        var auto = entries[1];
        Assert.Equal("AutoApproved", auto.Action);
        Assert.Equal(RequestStatus.Raised, auto.PreviousStatus);
        Assert.Equal(RequestStatus.Approved, auto.NewStatus);
        // The decision was made by the system, not by any user.
        Assert.Null(auto.ActorUserId);
        Assert.False(string.IsNullOrWhiteSpace(auto.Details));
        Assert.Equal(created.Id, auto.MaintenanceRequestId);
    }

    [Fact]
    public async Task Above_threshold_writes_RequestRaised_then_SubmittedForApproval_audit()
    {
        var alice = await ClientForAsync(Alice);
        var created = await CreateAsync(alice, HqTowerSiteId, "Audit: submitted", 1500m);

        var entries = await AuditTrailAsync(created.Id);

        Assert.Equal(2, entries.Count);

        var raised = entries[0];
        Assert.Equal("RequestRaised", raised.Action);
        Assert.Null(raised.PreviousStatus);
        Assert.Equal(RequestStatus.Raised, raised.NewStatus);
        Assert.Equal(created.RaisedByUserId, raised.ActorUserId);

        var submitted = entries[1];
        Assert.Equal("SubmittedForApproval", submitted.Action);
        Assert.Equal(RequestStatus.Raised, submitted.PreviousStatus);
        Assert.Equal(RequestStatus.PendingApproval, submitted.NewStatus);
        // Entering the queue is the creator's action, not a decision.
        Assert.Equal(created.RaisedByUserId, submitted.ActorUserId);
        Assert.False(string.IsNullOrWhiteSpace(submitted.Details));
    }

    // ------------------------------------------------------------------
    // Unauthenticated access
    // ------------------------------------------------------------------

    [Fact]
    public async Task Unauthenticated_requests_are_rejected()
    {
        Assert.Equal(HttpStatusCode.Unauthorized,
            await _anonymous.GetStatusAsync("/api/maintenance-requests"));

        var post = await _anonymous.PostAsJsonAsync("/api/maintenance-requests",
            new CreateMaintenanceRequestRequest(HqTowerSiteId, "Anon", "Created by test", 10m));
        Assert.Equal(HttpStatusCode.Unauthorized, post.StatusCode);
    }
}
