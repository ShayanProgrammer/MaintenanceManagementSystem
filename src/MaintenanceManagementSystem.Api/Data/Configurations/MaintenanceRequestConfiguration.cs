using MaintenanceManagementSystem.Api.Domain.Entities;
using MaintenanceManagementSystem.Api.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MaintenanceManagementSystem.Api.Data.Configurations;

public class MaintenanceRequestConfiguration : IEntityTypeConfiguration<MaintenanceRequest>
{
    public void Configure(EntityTypeBuilder<MaintenanceRequest> builder)
    {
        builder.ToTable("MaintenanceRequests");

        builder.HasKey(r => r.Id);

        builder.Property(r => r.Title)
            .HasMaxLength(200)
            .IsRequired();

        builder.Property(r => r.Description)
            .HasMaxLength(2000);

        builder.Property(r => r.EstimatedCost)
            .HasPrecision(18, 2);

        builder.Property(r => r.ActualCost)
            .HasPrecision(18, 2);

        builder.Property(r => r.Status)
            .HasConversion<string>()
            .HasMaxLength(30);

        builder.Property(r => r.RejectionReason)
            .HasMaxLength(1000);

        builder.HasIndex(r => new { r.OrganizationId, r.Status });
        builder.HasIndex(r => r.SiteId);
        builder.HasIndex(r => r.RaisedByUserId);

        // Tenant isolation depends on this column always matching the
        // request's site's organization; it is set server-side, never by the client.
        builder.HasOne(r => r.Organization)
            .WithMany(o => o.MaintenanceRequests)
            .HasForeignKey(r => r.OrganizationId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(r => r.Site)
            .WithMany(s => s.MaintenanceRequests)
            .HasForeignKey(r => r.SiteId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(r => r.RaisedBy)
            .WithMany(u => u.RaisedRequests)
            .HasForeignKey(r => r.RaisedByUserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(r => r.ApprovedBy)
            .WithMany(u => u.DecidedRequests)
            .HasForeignKey(r => r.ApprovedByUserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
