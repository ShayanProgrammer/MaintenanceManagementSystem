using MaintenanceManagementSystem.Api.Auth;
using MaintenanceManagementSystem.Api.Contracts;
using MaintenanceManagementSystem.Api.Data;
using MaintenanceManagementSystem.Api.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace MaintenanceManagementSystem.Api.Services;

/// <summary>
/// Organization-scoped spend reporting. Spend means actual money spent:
/// only Completed requests contribute, and only their ActualCost. The
/// aggregation (filtering, grouping, summing) runs entirely in the
/// database; the application never loads requests into memory for it.
/// </summary>
public class ReportService
{
    private readonly AppDbContext _db;
    private readonly TenantContext _tenant;

    public ReportService(AppDbContext db, TenantContext tenant)
    {
        _db = db;
        _tenant = tenant;
    }

    /// <summary>
    /// Total actual spend per site, for requests completed within the
    /// inclusive [from, to] date range. The range is applied to
    /// CompletedAtUtc as a half-open UTC interval — from 00:00:00Z
    /// (inclusive) through the start of the day after <paramref name="to"/>
    /// (exclusive) — so a completion at any time on the "to" date counts.
    /// The explicit organization predicate is defense in depth alongside the
    /// global query filter (decision 22). Sites with no qualifying completed
    /// spend in the range are omitted, and rows are ordered deterministically
    /// by SiteId.
    /// </summary>
    public async Task<List<SpendBySiteDto>> SpendBySiteAsync(DateOnly from, DateOnly to)
    {
        var startUtc = from.ToDateTime(TimeOnly.MinValue);                 // inclusive
        var exclusiveEndUtc = to.AddDays(1).ToDateTime(TimeOnly.MinValue); // exclusive

        // The aggregation is rooted at Site with a filtered SUM over its
        // request navigation: EF Core cannot translate a GROUP BY whose key
        // or join touches the Site side (query-filtered navigation), while
        // per-site correlated aggregation translates to one database-side
        // query. Sites with a zero/null total (no qualifying completed spend)
        // are filtered out; the remaining rows are ordered by SiteId. The
        // explicit organization predicate is defense in depth alongside the
        // global query filters (decision 22).
        return await _db.Sites
            .AsNoTracking()
            .Where(s => s.OrganizationId == _tenant.RequireOrganizationId())
            .Select(s => new
            {
                s.Id,
                s.Name,
                TotalSpend = s.MaintenanceRequests
                    .Where(r => r.Status == RequestStatus.Completed
                                && r.ActualCost != null
                                && r.CompletedAtUtc >= startUtc
                                && r.CompletedAtUtc < exclusiveEndUtc)
                    .Sum(r => r.ActualCost!.Value)
            })
            .Where(x => x.TotalSpend > 0)
            .OrderBy(x => x.Id)
            .Select(x => new SpendBySiteDto(x.Id, x.Name, x.TotalSpend))
            .ToListAsync();
    }
}
