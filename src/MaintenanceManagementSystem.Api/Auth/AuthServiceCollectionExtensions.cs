using System.Text;
using MaintenanceManagementSystem.Api.Auth;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace MaintenanceManagementSystem.Api;

public static class AuthServiceCollectionExtensions
{
    /// <summary>
    /// Registers JWT bearer authentication, the secure-by-default
    /// authorization fallback policy, named policies, JwtTokenService and
    /// TenantContext.
    ///
    /// JWT settings are bound from configuration and validated at host
    /// startup (ValidateOnStart) so misconfiguration fails fast — while
    /// still being read from the final configuration chain (user-secrets in
    /// development, in-memory/environment in tests and other environments).
    /// </summary>
    public static IServiceCollection AddAuthInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddOptions<JwtSettings>()
            .Bind(configuration.GetSection(JwtSettings.SectionName))
            .Validate(jwt =>
                    !string.IsNullOrWhiteSpace(jwt.Issuer)
                    && !string.IsNullOrWhiteSpace(jwt.Audience)
                    && !string.IsNullOrWhiteSpace(jwt.SigningKey)
                    && jwt.SigningKey.Length >= 32,
                "JWT configuration is missing or the signing key is too weak. " +
                "Set Jwt:Issuer, Jwt:Audience and Jwt:SigningKey (>= 32 chars), e.g. " +
                "dotnet user-secrets set \"Jwt:SigningKey\" \"<random-secret>\"")
            .ValidateOnStart();

        services.AddHttpContextAccessor();
        services.AddScoped<JwtTokenService>();
        services.AddScoped<TenantContext>();

        services
            .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer();

        // Token validation parameters are wired from the bound JwtSettings
        // when the bearer options are first resolved (after startup
        // validation has run).
        services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
            .Configure<IOptions<JwtSettings>>((options, jwtOptions) =>
            {
                var jwt = jwtOptions.Value;

                // Keep claim names exactly as written into the token
                // (otherwise "sub" would be remapped to a long XML URI).
                options.MapInboundClaims = false;

                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = jwt.Issuer,
                    ValidateAudience = true,
                    ValidAudience = jwt.Audience,
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.SigningKey)),
                    ValidateLifetime = true,
                    ClockSkew = TimeSpan.FromSeconds(30),
                    NameClaimType = AppClaimTypes.UserId,
                    RoleClaimType = AppClaimTypes.Role
                };
            });

        // Secure by default: every endpoint requires authentication unless it
        // is explicitly marked [AllowAnonymous] (login, /health, dev OpenAPI).
        services.AddAuthorization(options =>
        {
            options.FallbackPolicy = new AuthorizationPolicyBuilder()
                .RequireAuthenticatedUser()
                .Build();

            options.AddPolicy(AuthPolicies.ApproverOnly, policy =>
                policy.RequireRole("Approver"));
        });

        return services;
    }
}
