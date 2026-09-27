using MaintenanceManagementSystem.Api.Domain.Entities;
using MaintenanceManagementSystem.Api.Domain.Enums;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace MaintenanceManagementSystem.Api.Data;

/// <summary>
/// Development-only seed data. Runs at startup in the Development environment
/// and is a no-op if any data already exists (idempotent).
/// Two organizations are seeded so that tenant isolation can be demonstrated
/// and tested explicitly.
/// </summary>
public static class DbSeeder
{
    /// <summary>Known password for all seeded demo users (development only).</summary>
    public const string DemoPassword = "Pass123$";

    public static async Task SeedAsync(AppDbContext db)
    {
        // The seeder runs outside any authenticated request (system scope),
        // so tenant query filters must be bypassed here. With no tenant
        // identity a filtered Organizations query would match zero rows and
        // re-seed duplicates on every startup.
        if (await db.Organizations.IgnoreQueryFilters().AnyAsync())
        {
            return;
        }

        var passwordHasher = new PasswordHasher<User>();

        var northgate = new Organization
        {
            Name = "Northgate Facilities",
            ApprovalThreshold = 1000.00m
        };

        var summit = new Organization
        {
            Name = "Summit Property Group",
            ApprovalThreshold = 2500.00m
        };

        var northgateHq = new Site
        {
            Organization = northgate,
            Name = "HQ Tower",
            Description = "Main office tower, 12 floors"
        };

        var northgateDepot = new Site
        {
            Organization = northgate,
            Name = "Riverside Depot",
            Description = "Logistics depot and vehicle yard"
        };

        var summitPlaza = new Site
        {
            Organization = summit,
            Name = "Summit Plaza",
            Description = "Retail plaza with underground parking"
        };

        var alice = CreateUser(passwordHasher, northgate, "alice.requester@northgate.example", UserRole.Requester);
        var bob = CreateUser(passwordHasher, northgate, "bob.approver@northgate.example", UserRole.Approver);
        var carol = CreateUser(passwordHasher, summit, "carol.requester@summit.example", UserRole.Requester);
        var dave = CreateUser(passwordHasher, summit, "dave.approver@summit.example", UserRole.Approver);

        db.AddRange(northgate, summit, northgateHq, northgateDepot, summitPlaza, alice, bob, carol, dave);
        await db.SaveChangesAsync();
    }

    private static User CreateUser(
        PasswordHasher<User> passwordHasher,
        Organization organization,
        string email,
        UserRole role)
    {
        var user = new User
        {
            Organization = organization,
            Email = email,
            Role = role
        };

        user.PasswordHash = passwordHasher.HashPassword(user, DemoPassword);
        return user;
    }
}
