using MaintenanceManagementSystem.Api.Auth;
using MaintenanceManagementSystem.Api.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace MaintenanceManagementSystem.Api.Data;

public class AppDbContext : DbContext
{
    private readonly TenantContext _tenant;

    public AppDbContext(DbContextOptions<AppDbContext> options, TenantContext tenant) : base(options)
    {
        _tenant = tenant;
    }

    public DbSet<Organization> Organizations => Set<Organization>();
    public DbSet<Site> Sites => Set<Site>();
    public DbSet<User> Users => Set<User>();
    public DbSet<MaintenanceRequest> MaintenanceRequests => Set<MaintenanceRequest>();
    public DbSet<AuditEntry> AuditEntries => Set<AuditEntry>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);

        // ------------------------------------------------------------------
        // Tenant isolation.
        //
        // Every query against a tenant-owned entity is automatically
        // constrained to the authenticated user's organization. The filter
        // reads TenantContext (injected into this DbContext per request) at
        // query execution time, so the cached model stays correct across
        // requests from different organizations.
        //
        // TenantContext.OrganizationId is null outside authenticated
        // requests (seeding, login, background work). A null comparison
        // matches zero rows — default deny: a missing tenant identity can
        // never produce an unfiltered, all-tenants query.
        //
        // Users are intentionally NOT filtered: login queries users before
        // any authentication exists, and v1 has no user management
        // endpoints.
        // ------------------------------------------------------------------
        modelBuilder.Entity<Organization>().HasQueryFilter(o => o.Id == _tenant.OrganizationId);
        modelBuilder.Entity<Site>().HasQueryFilter(s => s.OrganizationId == _tenant.OrganizationId);
        modelBuilder.Entity<MaintenanceRequest>().HasQueryFilter(r => r.OrganizationId == _tenant.OrganizationId);
        modelBuilder.Entity<AuditEntry>().HasQueryFilter(a => a.OrganizationId == _tenant.OrganizationId);
    }
}
