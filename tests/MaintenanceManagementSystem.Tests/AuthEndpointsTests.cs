using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using MaintenanceManagementSystem.Api.Auth;
using MaintenanceManagementSystem.Api.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;

namespace MaintenanceManagementSystem.Tests;

public class AuthEndpointsTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _factory;
    private readonly HttpClient _client;

    public AuthEndpointsTests(ApiFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task Login_with_valid_credentials_returns_token_and_user()
    {
        var response = await _client.PostAsJsonAsync(
            "/api/auth/login",
            new LoginRequest("alice.requester@northgate.example", "Pass123$"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<LoginResponse>();
        Assert.NotNull(body);
        Assert.False(string.IsNullOrWhiteSpace(body!.AccessToken));
        Assert.True(body.ExpiresAtUtc > DateTime.UtcNow);
        Assert.Equal(1, body.User.Id);
        Assert.Equal(1, body.User.OrganizationId);
        Assert.Equal("alice.requester@northgate.example", body.User.Email);
        Assert.Equal("Requester", body.User.Role);
    }

    [Fact]
    public async Task Login_with_wrong_password_returns_401()
    {
        var response = await _client.PostAsJsonAsync(
            "/api/auth/login",
            new LoginRequest("alice.requester@northgate.example", "wrong-password"));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Login_with_unknown_email_returns_401()
    {
        var response = await _client.PostAsJsonAsync(
            "/api/auth/login",
            new LoginRequest("nobody@northgate.example", "Pass123$"));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Token_contains_user_organization_and_role_claims()
    {
        var response = await _client.PostAsJsonAsync(
            "/api/auth/login",
            new LoginRequest("bob.approver@northgate.example", "Pass123$"));

        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<LoginResponse>();

        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(body!.AccessToken);

        Assert.Equal("2", jwt.Claims.Single(c => c.Type == AppClaimTypes.UserId).Value);
        Assert.Equal("1", jwt.Claims.Single(c => c.Type == AppClaimTypes.OrganizationId).Value);
        Assert.Equal("Approver", jwt.Claims.Single(c => c.Type == AppClaimTypes.Role).Value);
        Assert.Equal("bob.approver@northgate.example", jwt.Claims.Single(c => c.Type == AppClaimTypes.Email).Value);
        Assert.Equal("test-issuer", jwt.Issuer);
    }

    [Fact]
    public async Task Me_without_token_returns_401()
    {
        var response = await _client.GetAsync("/api/auth/me");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Me_with_valid_token_returns_the_identity_in_the_token()
    {
        var login = await _client.PostAsJsonAsync(
            "/api/auth/login",
            new LoginRequest("alice.requester@northgate.example", "Pass123$"));
        login.EnsureSuccessStatusCode();
        var token = (await login.Content.ReadFromJsonAsync<LoginResponse>())!.AccessToken;

        _client.DefaultRequestHeaders.Authorization = new("Bearer", token);
        var response = await _client.GetAsync("/api/auth/me");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var me = await response.Content.ReadFromJsonAsync<MeResponse>();
        Assert.NotNull(me);
        Assert.Equal(1, me!.UserId);
        Assert.Equal(1, me.OrganizationId);
        Assert.Equal("alice.requester@northgate.example", me.Email);
        Assert.Equal("Requester", me.Role);
    }

    [Fact]
    public async Task Health_endpoint_is_anonymous()
    {
        var response = await _client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    /// <summary>
    /// The ApproverOnly policy must reject Requesters and allow Approvers.
    /// Evaluated through the DI-registered IAuthorizationService because no
    /// approver-only HTTP endpoint exists yet (phase 3 adds those, along with
    /// HTTP-level 403 integration tests).
    /// </summary>
    [Fact]
    public async Task ApproverOnly_policy_rejects_requester_and_allows_approver()
    {
        var services = _factory.Services;
        var policyProvider = services.GetRequiredService<IAuthorizationPolicyProvider>();
        var authorizationService = services.GetRequiredService<IAuthorizationService>();

        var policy = await policyProvider.GetPolicyAsync(AuthPolicies.ApproverOnly);
        Assert.NotNull(policy);

        var requester = PrincipalWithRole("Requester");
        var approver = PrincipalWithRole("Approver");

        Assert.False((await authorizationService.AuthorizeAsync(requester, policy!)).Succeeded);
        Assert.True((await authorizationService.AuthorizeAsync(approver, policy!)).Succeeded);
    }

    private static ClaimsPrincipal PrincipalWithRole(string role) =>
        new(new ClaimsIdentity(
            new[] { new Claim(AppClaimTypes.UserId, "1"), new Claim(AppClaimTypes.Role, role) },
            authenticationType: "Test",
            nameType: null,
            roleType: AppClaimTypes.Role));
}
