using MaintenanceManagementSystem.Api;
using MaintenanceManagementSystem.Api.Data;
using MaintenanceManagementSystem.Api.Services;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

builder.Services.AddControllers();

builder.Services.AddScoped<SiteService>();
builder.Services.AddScoped<OrganizationService>();
builder.Services.AddScoped<AuditService>();
builder.Services.AddScoped<MaintenanceRequestService>();
builder.Services.AddScoped<ReportService>();

builder.Services.AddOpenApi();

// JWT bearer authentication, secure-by-default authorization fallback
// policy, ApproverOnly policy, JwtTokenService and TenantContext.
builder.Services.AddAuthInfrastructure(builder.Configuration);

// Connection string comes from user-secrets (never from source control).
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("Default")));

var app = builder.Build();

// Configure the HTTP request pipeline.

if (app.Environment.IsDevelopment())
{
    // Anonymous so the OpenAPI document can be browsed while developing;
    // the API endpoints themselves remain authenticated by default.
    app.MapOpenApi().AllowAnonymous();

    // Development convenience: apply migrations and seed demo data at startup.
    // The seeder is a no-op once data exists.
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await db.Database.MigrateAsync();
    await DbSeeder.SeedAsync(db);
}

app.UseHttpsRedirection();

app.UseAuthentication();

app.UseAuthorization();

app.MapControllers();

app.MapGet("/health", () => Results.Ok(new { status = "ok" }))
    .AllowAnonymous();

app.Run();

// Makes the implicit Program class visible to WebApplicationFactory in tests.
public partial class Program { }
