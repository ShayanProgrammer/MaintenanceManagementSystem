namespace MaintenanceManagementSystem.Api.Contracts;

// Spend report read shape. Projected from a database-side GROUP BY — EF
// entities are never returned directly. The organization is implicit: every
// row belongs to the caller's organization by construction.

public record SpendBySiteDto(
    int SiteId,
    string SiteName,
    decimal TotalSpend);
