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
/// GET /api/reports/spend: organization-scoped, ActualCost-based spend per
/// site for Completed requests, filtered by inclusive CompletedAtUtc date
/// range. All spend is created through the public API; because completion
/// always happens "now", tests backdate CompletedAtUtc through direct test
/// setup (each test owns a disjoint month of 2026, so every queried range
/// isolates its own data — order-independent). Test-only database access is
/// confined to the two backdating helpers below.
/// Seeded identities: org 1 (Northgate, threshold 1000): sites 1 HQ Tower,
/// 2 Riverside Depot — alice requester, bob approver; org 2 (Summit,
/// threshold 2500): site 3 Summit Plaza — carol requester, dave approver.
/// </summary>
public class SpendReportTests : IClassFixture<ApiFactory>
{
    private const string Alice = "alice.requester@northgate.example";
    private const string Bob = "bob.approver@northgate.example";
    private const string Carol = "carol.requester@summit.example";
    private const string Dave = "dave.approver@summit.example";

    private const int HqTowerSiteId = 1;
    private const int RiversideDepotSiteId = 2;
    private const int SummitPlazaSiteId = 3;

    private readonly ApiFactory _factory;
    private readonly HttpClient _anonymous;

    public SpendReportTests(ApiFactory factory)
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

    /// <summary>Creates an auto-approved request (cost at/below threshold).</summary>
    private async Task<MaintenanceRequestDto> CreateAutoApprovedAsync(
        HttpClient client, int siteId, string title, decimal cost)
    {
        var response = await client.PostAsJsonAsync("/api/maintenance-requests",
            new CreateMaintenanceRequestRequest(siteId, title, "Created by test", cost));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<MaintenanceRequestDto>())!;
    }

    private async Task<MaintenanceRequestDto> CreatePendingAsync(
        HttpClient client, int siteId, string title, decimal cost)
    {
        var response = await client.PostAsJsonAsync("/api/maintenance-requests",
            new CreateMaintenanceRequestRequest(siteId, title, "Created by test", cost));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = (await response.Content.ReadFromJsonAsync<MaintenanceRequestDto>())!;
        Assert.Equal("PendingApproval", created.Status);
        return created;
    }

    private async Task<MaintenanceRequestDto> CompleteAsync(
        HttpClient client, int requestId, decimal actualCost)
    {
        var response = await client.PostAsJsonAsync(
            $"/api/maintenance-requests/{requestId}/completion",
            new CreateCompletionRequest(actualCost));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<MaintenanceRequestDto>())!;
    }

    private Task<HttpResponseMessage> DecideAsync(
        HttpClient client, int id, ApprovalDecision decision, string? reason) =>
        client.PostAsJsonAsync($"/api/maintenance-requests/{id}/approvals",
            new CreateApprovalDecisionRequest(decision, reason));

    private async Task<List<SpendBySiteDto>> ReportAsync(
        HttpClient client, string from, string to) =>
        await client.GetFromJsonAsync<List<SpendBySiteDto>>(
            $"/api/reports/spend?from={from}&to={to}") ?? [];

    /// <summary>Test-only setup: the API always completes "now"; the report's
    /// date-range logic is exercised by backdating the completion date of an
    /// already-completed request directly in the database.</summary>
    private async Task BackdateCompletionAsync(int requestId, DateTime utc)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var request = await db.MaintenanceRequests
            .IgnoreQueryFilters()
            .SingleAsync(r => r.Id == requestId);
        request.CompletedAtUtc = utc;
        await db.SaveChangesAsync();
    }

    /// <summary>Test-only setup: nulls ActualCost so the "null ActualCost
    /// never contributes" rule can be exercised despite the API always
    /// requiring a value at completion.</summary>
    private async Task ClearActualCostAsync(int requestId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var request = await db.MaintenanceRequests
            .IgnoreQueryFilters()
            .SingleAsync(r => r.Id == requestId);
        request.ActualCost = null;
        await db.SaveChangesAsync();
    }

    // ------------------------------------------------------------------
    // Authorization
    // ------------------------------------------------------------------

    [Fact]
    public async Task Requester_can_access_spend_report()
    {
        var alice = await ClientForAsync(Alice);

        Assert.Equal(HttpStatusCode.OK,
            (await alice.GetAsync("/api/reports/spend?from=2000-01-01&to=2099-12-31")).StatusCode);
    }

    [Fact]
    public async Task Approver_can_access_spend_report()
    {
        var bob = await ClientForAsync(Bob);

        Assert.Equal(HttpStatusCode.OK,
            (await bob.GetAsync("/api/reports/spend?from=2000-01-01&to=2099-12-31")).StatusCode);
    }

    [Fact]
    public async Task Anonymous_requests_are_rejected()
    {
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await _anonymous.GetAsync("/api/reports/spend?from=2026-01-01&to=2026-12-31")).StatusCode);
    }

    // ------------------------------------------------------------------
    // Which requests contribute
    // ------------------------------------------------------------------

    [Fact]
    public async Task Completed_request_actual_cost_is_included()
    {
        var alice = await ClientForAsync(Alice);
        var request = await CreateAutoApprovedAsync(alice, HqTowerSiteId, "March spend", 250m);
        await CompleteAsync(alice, request.Id, 250m);
        await BackdateCompletionAsync(request.Id, new DateTime(2026, 3, 15, 10, 0, 0, DateTimeKind.Utc));

        var report = await ReportAsync(alice, "2026-03-01", "2026-03-31");

        var entry = report.Single(s => s.SiteId == HqTowerSiteId);
        Assert.Equal("HQ Tower", entry.SiteName);
        Assert.Equal(250m, entry.TotalSpend);
        // Zero-spend site omitted: Riverside Depot has no qualifying spend in March.
        Assert.DoesNotContain(report, s => s.SiteId == RiversideDepotSiteId);
    }

    [Fact]
    public async Task Non_qualifying_requests_do_not_contribute()
    {
        var alice = await ClientForAsync(Alice);
        var bob = await ClientForAsync(Bob);

        // PendingApproval.
        await CreatePendingAsync(alice, HqTowerSiteId, "October: pending", 1500m);

        // Approved but never completed.
        var approved = await CreatePendingAsync(alice, HqTowerSiteId, "October: approved only", 1500m);
        await DecideAsync(bob, approved.Id, ApprovalDecision.Approve, null);

        // Rejected.
        var rejected = await CreatePendingAsync(alice, HqTowerSiteId, "October: rejected", 1500m);
        await DecideAsync(bob, rejected.Id, ApprovalDecision.Reject, "Not now");

        Assert.Empty(await ReportAsync(alice, "2026-10-01", "2026-10-31"));
    }

    [Fact]
    public async Task Completed_request_with_null_actual_cost_does_not_contribute()
    {
        var alice = await ClientForAsync(Alice);
        var request = await CreateAutoApprovedAsync(alice, HqTowerSiteId, "September: no actual", 250m);
        await CompleteAsync(alice, request.Id, 250m);
        await BackdateCompletionAsync(request.Id, new DateTime(2026, 9, 10, 0, 0, 0, DateTimeKind.Utc));
        await ClearActualCostAsync(request.Id);

        Assert.Empty(await ReportAsync(alice, "2026-09-01", "2026-09-30"));
    }

    // ------------------------------------------------------------------
    // Grouping and summation
    // ------------------------------------------------------------------

    [Fact]
    public async Task Spend_is_grouped_by_site()
    {
        var alice = await ClientForAsync(Alice);

        var atHq = await CreateAutoApprovedAsync(alice, HqTowerSiteId, "April HQ", 250m);
        await CompleteAsync(alice, atHq.Id, 250m);
        await BackdateCompletionAsync(atHq.Id, new DateTime(2026, 4, 10, 0, 0, 0, DateTimeKind.Utc));

        var atDepot = await CreateAutoApprovedAsync(alice, RiversideDepotSiteId, "April depot", 100m);
        await CompleteAsync(alice, atDepot.Id, 100m);
        await BackdateCompletionAsync(atDepot.Id, new DateTime(2026, 4, 20, 0, 0, 0, DateTimeKind.Utc));

        var report = await ReportAsync(alice, "2026-04-01", "2026-04-30");

        Assert.Equal(2, report.Count);
        Assert.Equal(HqTowerSiteId, report[0].SiteId);           // deterministic SiteId order
        Assert.Equal(250m, report[0].TotalSpend);
        Assert.Equal(RiversideDepotSiteId, report[1].SiteId);
        Assert.Equal(100m, report[1].TotalSpend);
    }

    [Fact]
    public async Task Multiple_completed_requests_for_same_site_are_summed()
    {
        var alice = await ClientForAsync(Alice);

        var first = await CreateAutoApprovedAsync(alice, HqTowerSiteId, "May spend 1", 250m);
        await CompleteAsync(alice, first.Id, 250m);
        await BackdateCompletionAsync(first.Id, new DateTime(2026, 5, 10, 0, 0, 0, DateTimeKind.Utc));

        var second = await CreateAutoApprovedAsync(alice, HqTowerSiteId, "May spend 2", 300m);
        await CompleteAsync(alice, second.Id, 300m);
        await BackdateCompletionAsync(second.Id, new DateTime(2026, 5, 20, 0, 0, 0, DateTimeKind.Utc));

        var report = await ReportAsync(alice, "2026-05-01", "2026-05-31");

        var entry = report.Single(s => s.SiteId == HqTowerSiteId);
        Assert.Equal(550m, entry.TotalSpend);
    }

    // ------------------------------------------------------------------
    // Date range semantics (inclusive from and to; half-open UTC interval)
    // ------------------------------------------------------------------

    [Fact]
    public async Task From_date_start_is_included()
    {
        var alice = await ClientForAsync(Alice);
        var request = await CreateAutoApprovedAsync(alice, HqTowerSiteId, "June start", 100m);
        await CompleteAsync(alice, request.Id, 100m);
        // Completed 30 seconds after midnight on the from date.
        await BackdateCompletionAsync(request.Id, new DateTime(2026, 6, 1, 0, 0, 30, DateTimeKind.Utc));

        var report = await ReportAsync(alice, "2026-06-01", "2026-06-30");

        Assert.Equal(100m, report.Single(s => s.SiteId == HqTowerSiteId).TotalSpend);
    }

    [Fact]
    public async Task To_date_end_is_included()
    {
        var alice = await ClientForAsync(Alice);
        var request = await CreateAutoApprovedAsync(alice, HqTowerSiteId, "July end", 100m);
        await CompleteAsync(alice, request.Id, 100m);
        // Completed one second before midnight ending the to date — a naive
        // time-of-day comparison against the to date would exclude this.
        await BackdateCompletionAsync(request.Id, new DateTime(2026, 7, 31, 23, 59, 59, DateTimeKind.Utc));

        var report = await ReportAsync(alice, "2026-07-01", "2026-07-31");

        Assert.Equal(100m, report.Single(s => s.SiteId == HqTowerSiteId).TotalSpend);
    }

    [Fact]
    public async Task Completions_outside_the_range_are_excluded()
    {
        var alice = await ClientForAsync(Alice);

        var before = await CreateAutoApprovedAsync(alice, HqTowerSiteId, "August: early", 250m);
        await CompleteAsync(alice, before.Id, 250m);
        await BackdateCompletionAsync(before.Id, new DateTime(2026, 8, 5, 0, 0, 0, DateTimeKind.Utc));

        var after = await CreateAutoApprovedAsync(alice, RiversideDepotSiteId, "August: late", 400m);
        await CompleteAsync(alice, after.Id, 400m);
        await BackdateCompletionAsync(after.Id, new DateTime(2026, 8, 25, 0, 0, 0, DateTimeKind.Utc));

        // The range 08-10..08-15 sits between the two completions: nothing qualifies.
        Assert.Empty(await ReportAsync(alice, "2026-08-10", "2026-08-15"));

        // Sanity: each completion is caught by a range that covers it.
        Assert.Equal(250m, (await ReportAsync(alice, "2026-08-01", "2026-08-09")).Single().TotalSpend);
        Assert.Equal(400m, (await ReportAsync(alice, "2026-08-24", "2026-08-31")).Single().TotalSpend);
    }

    // ------------------------------------------------------------------
    // Validation
    // ------------------------------------------------------------------

    [Fact]
    public async Task Invalid_ranges_return_400()
    {
        var alice = await ClientForAsync(Alice);

        // from after to — never silently swapped.
        Assert.Equal(HttpStatusCode.BadRequest,
            (await alice.GetAsync("/api/reports/spend?from=2026-03-31&to=2026-03-01")).StatusCode);

        // Missing from / missing to / both.
        Assert.Equal(HttpStatusCode.BadRequest,
            (await alice.GetAsync("/api/reports/spend?to=2026-03-31")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await alice.GetAsync("/api/reports/spend?from=2026-03-01")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await alice.GetAsync("/api/reports/spend")).StatusCode);

        // Unparseable dates.
        Assert.Equal(HttpStatusCode.BadRequest,
            (await alice.GetAsync("/api/reports/spend?from=not-a-date&to=2026-03-31")).StatusCode);
    }

    // ------------------------------------------------------------------
    // Tenant isolation (the critical invariant)
    // ------------------------------------------------------------------

    [Fact]
    public async Task Spend_is_never_reported_across_organizations()
    {
        var alice = await ClientForAsync(Alice);
        var bob = await ClientForAsync(Bob);
        var carol = await ClientForAsync(Carol);
        var dave = await ClientForAsync(Dave);

        // Northgate: HQ Tower -> 1000, Riverside Depot -> 500.
        var northgateHq = await CreateAutoApprovedAsync(alice, HqTowerSiteId, "Nov HQ", 1000m);
        await CompleteAsync(alice, northgateHq.Id, 1000m);
        await BackdateCompletionAsync(northgateHq.Id, new DateTime(2026, 11, 15, 0, 0, 0, DateTimeKind.Utc));

        var northgateDepot = await CreateAutoApprovedAsync(alice, RiversideDepotSiteId, "Nov depot", 500m);
        await CompleteAsync(alice, northgateDepot.Id, 500m);
        await BackdateCompletionAsync(northgateDepot.Id, new DateTime(2026, 11, 16, 0, 0, 0, DateTimeKind.Utc));

        // Summit: Summit Plaza -> 7000.
        var summitRequest = await CreatePendingAsync(carol, SummitPlazaSiteId, "Nov summit", 7500m);
        await DecideAsync(dave, summitRequest.Id, ApprovalDecision.Approve, null);
        await CompleteAsync(carol, summitRequest.Id, 7000m);
        await BackdateCompletionAsync(summitRequest.Id, new DateTime(2026, 11, 17, 0, 0, 0, DateTimeKind.Utc));

        // Alice sees exactly the Northgate sites and amounts — and nothing from Summit.
        var northgateReport = await ReportAsync(alice, "2026-11-01", "2026-11-30");
        Assert.Equal(2, northgateReport.Count);
        Assert.Equal(HqTowerSiteId, northgateReport[0].SiteId);
        Assert.Equal(1000m, northgateReport[0].TotalSpend);
        Assert.Equal(RiversideDepotSiteId, northgateReport[1].SiteId);
        Assert.Equal(500m, northgateReport[1].TotalSpend);
        Assert.DoesNotContain(northgateReport, s => s.SiteId == SummitPlazaSiteId);
        Assert.DoesNotContain(northgateReport, s => s.SiteName == "Summit Plaza");

        // Carol sees exactly the Summit spend — and nothing from Northgate.
        var summitReport = await ReportAsync(carol, "2026-11-01", "2026-11-30");
        Assert.Equal(1, summitReport.Count);
        Assert.Equal(SummitPlazaSiteId, summitReport[0].SiteId);
        Assert.Equal(7000m, summitReport[0].TotalSpend);
        Assert.DoesNotContain(summitReport, s => s.SiteId == HqTowerSiteId);
        Assert.DoesNotContain(summitReport, s => s.SiteId == RiversideDepotSiteId);
        Assert.DoesNotContain(summitReport, s => s.SiteName == "HQ Tower");

        // Same story for approvers (Bob/Dave add nothing themselves here).
        Assert.DoesNotContain(await ReportAsync(bob, "2026-11-01", "2026-11-30"),
            s => s.SiteId == SummitPlazaSiteId);
    }

    // ------------------------------------------------------------------
    // Decimal precision
    // ------------------------------------------------------------------

    [Fact]
    public async Task Decimal_totals_are_preserved()
    {
        var alice = await ClientForAsync(Alice);

        var first = await CreateAutoApprovedAsync(alice, HqTowerSiteId, "Dec cents 1", 33.33m);
        await CompleteAsync(alice, first.Id, 33.33m);
        await BackdateCompletionAsync(first.Id, new DateTime(2026, 12, 10, 0, 0, 0, DateTimeKind.Utc));

        var second = await CreateAutoApprovedAsync(alice, HqTowerSiteId, "Dec cents 2", 0.07m);
        await CompleteAsync(alice, second.Id, 0.07m);
        await BackdateCompletionAsync(second.Id, new DateTime(2026, 12, 20, 0, 0, 0, DateTimeKind.Utc));

        var report = await ReportAsync(alice, "2026-12-01", "2026-12-31");

        Assert.Equal(33.40m, report.Single(s => s.SiteId == HqTowerSiteId).TotalSpend);
    }
}
