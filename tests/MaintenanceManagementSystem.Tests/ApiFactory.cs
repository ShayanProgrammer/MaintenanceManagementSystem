using MaintenanceManagementSystem.Api.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace MaintenanceManagementSystem.Tests;

/// <summary>
/// Hosts the real API in-memory for integration tests:
/// - environment "Testing" skips the Development-only startup block
///   (SQL Server migrations + dev seeding)
/// - SQL Server is replaced with an in-memory SQLite database
///   (schema created from the EF model via EnsureCreated)
/// - JWT settings are supplied in-memory because user-secrets are
///   only loaded in the Development environment
/// </summary>
public class ApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    public const string TestSigningKey = "test-signing-key-0123456789abcdef-not-a-production-secret";

    private readonly SqliteConnection _connection = new("DataSource=:memory:");

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        builder.ConfigureAppConfiguration((_, configuration) =>
        {
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jwt:Issuer"] = "test-issuer",
                ["Jwt:Audience"] = "test-audience",
                ["Jwt:SigningKey"] = TestSigningKey,
                ["Jwt:LifetimeMinutes"] = "60"
            });
        });

        builder.ConfigureServices(services =>
        {
            // Replace the SQL Server provider with in-memory SQLite.
            services.RemoveAll<DbContextOptions<AppDbContext>>();
            services.RemoveAll(typeof(IDbContextOptionsConfiguration<AppDbContext>));
            services.AddDbContext<AppDbContext>(options => options.UseSqlite(_connection));
        });
    }

    public async Task InitializeAsync()
    {
        // Keep one open connection for the factory lifetime so the
        // in-memory database survives across scopes/requests.
        await _connection.OpenAsync();

        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.EnsureCreatedAsync();
        await DbSeeder.SeedAsync(db);
    }

    public new async Task DisposeAsync()
    {
        await base.DisposeAsync();
        await _connection.DisposeAsync();
    }
}
