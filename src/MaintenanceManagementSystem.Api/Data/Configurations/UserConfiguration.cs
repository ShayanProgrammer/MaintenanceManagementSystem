using MaintenanceManagementSystem.Api.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MaintenanceManagementSystem.Api.Data.Configurations;

public class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.ToTable("Users");

        builder.HasKey(u => u.Id);

        builder.Property(u => u.Email)
            .HasMaxLength(256)
            .IsRequired();

        // Login identity; stores the ASP.NET Core PasswordHasher format.
        builder.Property(u => u.PasswordHash)
            .HasMaxLength(500)
            .IsRequired();

        // Stored as a readable string (e.g. "Approver") rather than an int.
        builder.Property(u => u.Role)
            .HasConversion<string>()
            .HasMaxLength(20);

        // Email is the login name and must be unique across the whole system.
        builder.HasIndex(u => u.Email).IsUnique();
        builder.HasIndex(u => u.OrganizationId);

        builder.HasOne(u => u.Organization)
            .WithMany(o => o.Users)
            .HasForeignKey(u => u.OrganizationId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
