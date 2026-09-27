using MaintenanceManagementSystem.Api.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MaintenanceManagementSystem.Api.Data.Configurations;

public class AuditEntryConfiguration : IEntityTypeConfiguration<AuditEntry>
{
    public void Configure(EntityTypeBuilder<AuditEntry> builder)
    {
        builder.ToTable("AuditEntries");

        builder.HasKey(a => a.Id);

        builder.Property(a => a.Action)
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(a => a.PreviousStatus)
            .HasConversion<string>()
            .HasMaxLength(30);

        builder.Property(a => a.NewStatus)
            .HasConversion<string>()
            .HasMaxLength(30);

        builder.Property(a => a.Details)
            .HasMaxLength(2000);

        // Sorted listing is the primary read pattern.
        builder.HasIndex(a => new { a.OrganizationId, a.MaintenanceRequestId });
        builder.HasIndex(a => a.TimestampUtc);

        builder.HasOne(a => a.Organization)
            .WithMany(o => o.AuditEntries)
            .HasForeignKey(a => a.OrganizationId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(a => a.MaintenanceRequest)
            .WithMany(r => r.AuditEntries)
            .HasForeignKey(a => a.MaintenanceRequestId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(a => a.Actor)
            .WithMany(u => u.AuditEntries)
            .HasForeignKey(a => a.ActorUserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
