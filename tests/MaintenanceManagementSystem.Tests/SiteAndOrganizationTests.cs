using System.Net;
using System.Net.Http.Json;
using MaintenanceManagementSystem.Api.Contracts;
using Xunit;

namespace MaintenanceManagementSystem.Tests;

/// <summary>
/// Tenant isolation and authorization for the organization/site endpoints.
/// Seeded identities:
///   org 1 (Northgate Facilities):  1 = HQ Tower, 2 = Riverside Depot
///     alice.requester@northgate.example (Requester)
///     bob.approver@northgate.example    (Approver)
///   org 2 (Summit Property Group): 3 = Summit Plaza
///     carol.requester@summit.example    (Requester)
///     dave.approver@summit.example      (Approver)
///
/// Org 1 and org 2 requests intentionally share one factory/host so that
/// cross-tenant leakage and stale tenant state are covered.
/// </summary>
public class SiteAndOrganizationTests : IClassFixture<ApiFactory>
{
    private const string Alice = "alice.requester@northgate.example";
    private const string Bob = "bob.approver@northgate.example";
    private const string Carol = "carol.requester@summit.example";
    private const string Dave = "dave.approver@summit.example";

    private const int SummitPlazaSiteId = 3;

    private readonly ApiFactory _factory;
    private readonly HttpClient _anonymous;

    public SiteAndOrganizationTests(ApiFactory factory)
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

    // ------------------------------------------------------------------
    // Organization isolation
    // ------------------------------------------------------------------

    [Fact]
    public async Task Current_organization_returns_org_1_for_org_1_user()
    {
        var alice = await ClientForAsync(Alice);

        var org = await alice.GetFromJsonAsync<OrganizationDto>("/api/organizations/current");

        Assert.NotNull(org);
        Assert.Equal(1, org!.Id);
        Assert.Equal("Northgate Facilities", org.Name);
    }

    [Fact]
    public async Task Current_organization_returns_org_2_for_org_2_user()
    {
        var carol = await ClientForAsync(Carol);

        var org = await carol.GetFromJsonAsync<OrganizationDto>("/api/organizations/current");

        Assert.NotNull(org);
        Assert.Equal(2, org!.Id);
        Assert.Equal("Summit Property Group", org.Name);
    }

    [Fact]
    public async Task Organization_1_user_cannot_retrieve_organization_2_by_id()
    {
        var alice = await ClientForAsync(Alice);

        // No /api/organizations/{id} route exists at all — manipulating ids
        // in the URL has no target.
        Assert.Equal(HttpStatusCode.NotFound, await alice.GetStatusAsync("/api/organizations/2"));
        Assert.Equal(HttpStatusCode.NotFound, await alice.GetStatusAsync("/api/organizations"));
    }

    // ------------------------------------------------------------------
    // Site isolation
    // ------------------------------------------------------------------

    [Fact]
    public async Task Org_1_user_sees_only_org_1_sites()
    {
        var alice = await ClientForAsync(Alice);

        var sites = await alice.GetFromJsonAsync<List<SiteDto>>("/api/sites");

        Assert.NotNull(sites);
        // Order-independent isolation invariants: only org-1 rows, the seeded
        // org-1 sites are visible, and no org-2 site ever is. (Other tests in
        // this class may have added extra org-1 sites before this one runs.)
        Assert.All(sites!, s => Assert.Equal(1, s.OrganizationId));
        Assert.Contains(sites!, s => s.Name == "HQ Tower");
        Assert.Contains(sites!, s => s.Name == "Riverside Depot");
        Assert.DoesNotContain(sites!, s => s.Name == "Summit Plaza");
    }

    [Fact]
    public async Task Org_2_user_sees_only_org_2_sites()
    {
        var carol = await ClientForAsync(Carol);

        var sites = await carol.GetFromJsonAsync<List<SiteDto>>("/api/sites");

        Assert.NotNull(sites);
        Assert.All(sites!, s => Assert.Equal(2, s.OrganizationId));
        Assert.Contains(sites!, s => s.Name == "Summit Plaza");
        Assert.DoesNotContain(sites!, s => s.Name == "HQ Tower");
        Assert.DoesNotContain(sites!, s => s.Name == "Riverside Depot");
    }

    [Fact]
    public async Task Org_1_approver_cannot_update_org_2_site_by_id()
    {
        var bob = await ClientForAsync(Bob);

        var response = await bob.PutAsJsonAsync(
            $"/api/sites/{SummitPlazaSiteId}",
            new UpdateSiteRequest("Hacked Site", null));

        // 404, not 403 — the site of another tenant is indistinguishable
        // from an unknown site.
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

        // And the site is untouched.
        var carol = await ClientForAsync(Carol);
        var sites = await carol.GetFromJsonAsync<List<SiteDto>>("/api/sites");
        var summit = sites!.Single(s => s.Id == SummitPlazaSiteId);
        Assert.Equal("Summit Plaza", summit.Name);
        Assert.Equal("Retail plaza with underground parking", summit.Description);
    }

    [Fact]
    public async Task OrganizationId_in_create_body_cannot_control_tenancy()
    {
        var bob = await ClientForAsync(Bob);

        // The JSON contains organizationId: 2, but CreateSiteRequest has no
        // such property — the binding ignores it and the server assigns
        // org 1 (Bob's tenant).
        var response = await bob.PostAsJsonAsync("/api/sites", new
        {
            organizationId = 2,
            name = "North Warehouse",
            description = "Created by org 1 approver"
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = await response.Content.ReadFromJsonAsync<SiteDto>();
        Assert.NotNull(created);
        Assert.Equal(1, created!.OrganizationId);
        Assert.Equal("North Warehouse", created.Name);

        // Org 2 must not see the new site.
        var carol = await ClientForAsync(Carol);
        var org2Sites = await carol.GetFromJsonAsync<List<SiteDto>>("/api/sites");
        Assert.DoesNotContain(org2Sites!, s => s.Name == "North Warehouse");
    }

    // ------------------------------------------------------------------
    // Authorization (Requester vs Approver)
    // ------------------------------------------------------------------

    [Fact]
    public async Task Requester_can_list_sites()
    {
        var alice = await ClientForAsync(Alice);

        Assert.Equal(HttpStatusCode.OK, await alice.GetStatusAsync("/api/sites"));
    }

    [Fact]
    public async Task Requester_cannot_create_site()
    {
        var alice = await ClientForAsync(Alice);

        var response = await alice.PostAsJsonAsync(
            "/api/sites", new CreateSiteRequest("Nope", null));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Requester_cannot_update_site()
    {
        var alice = await ClientForAsync(Alice);

        var response = await alice.PutAsJsonAsync(
            "/api/sites/1", new UpdateSiteRequest("Nope", null));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Requester_cannot_change_approval_threshold()
    {
        var alice = await ClientForAsync(Alice);

        var response = await alice.PutAsJsonAsync(
            "/api/organizations/current/threshold", new UpdateThresholdRequest(5000m));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Approver_can_create_and_update_own_site()
    {
        var bob = await ClientForAsync(Bob);

        var createResponse = await bob.PostAsJsonAsync(
            "/api/sites", new CreateSiteRequest("Test Depot", "Created by test"));
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        var created = await createResponse.Content.ReadFromJsonAsync<SiteDto>();
        Assert.Equal(1, created!.OrganizationId);

        var updateResponse = await bob.PutAsJsonAsync(
            $"/api/sites/{created.Id}", new UpdateSiteRequest("Test Depot Renamed", null));
        Assert.Equal(HttpStatusCode.OK, updateResponse.StatusCode);
        var updated = await updateResponse.Content.ReadFromJsonAsync<SiteDto>();
        Assert.Equal("Test Depot Renamed", updated!.Name);
    }

    [Fact]
    public async Task Approver_can_change_own_organization_threshold()
    {
        var bob = await ClientForAsync(Bob);

        var response = await bob.PutAsJsonAsync(
            "/api/organizations/current/threshold", new UpdateThresholdRequest(1500m));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var org = await response.Content.ReadFromJsonAsync<OrganizationDto>();
        Assert.Equal(1, org!.Id);
        Assert.Equal(1500m, org.ApprovalThreshold);
    }

    // ------------------------------------------------------------------
    // Threshold isolation
    // ------------------------------------------------------------------

    [Fact]
    public async Task Changing_org_1_threshold_does_not_affect_org_2()
    {
        var bob = await ClientForAsync(Bob);
        var dave = await ClientForAsync(Dave);

        var org2Before = await dave.GetFromJsonAsync<OrganizationDto>("/api/organizations/current");

        var update = await bob.PutAsJsonAsync(
            "/api/organizations/current/threshold", new UpdateThresholdRequest(4242m));
        Assert.Equal(HttpStatusCode.OK, update.StatusCode);

        var org2After = await dave.GetFromJsonAsync<OrganizationDto>("/api/organizations/current");
        Assert.Equal(org2Before!.ApprovalThreshold, org2After!.ApprovalThreshold);
        Assert.Equal(2, org2After.Id);

        var org1 = await bob.GetFromJsonAsync<OrganizationDto>("/api/organizations/current");
        Assert.Equal(4242m, org1!.ApprovalThreshold);
    }

    // ------------------------------------------------------------------
    // Unauthenticated access
    // ------------------------------------------------------------------

    [Fact]
    public async Task Unauthenticated_requests_are_rejected()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, await _anonymous.GetStatusAsync("/api/sites"));
        Assert.Equal(HttpStatusCode.Unauthorized,
            await _anonymous.GetStatusAsync("/api/organizations/current"));

        var post = await _anonymous.PostAsJsonAsync("/api/sites", new CreateSiteRequest("X", null));
        Assert.Equal(HttpStatusCode.Unauthorized, post.StatusCode);

        var threshold = await _anonymous.PutAsJsonAsync(
            "/api/organizations/current/threshold", new UpdateThresholdRequest(1m));
        Assert.Equal(HttpStatusCode.Unauthorized, threshold.StatusCode);
    }
}

internal static class HttpClientStatusExtensions
{
    public static async Task<HttpStatusCode> GetStatusAsync(this HttpClient client, string url) =>
        (await client.GetAsync(url)).StatusCode;
}
