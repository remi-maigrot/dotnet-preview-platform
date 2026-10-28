using Microsoft.EntityFrameworkCore;
using Sample.Application.Absences;
using Sample.Application.Abstractions;
using Sample.Application.Common;
using Sample.Domain.Absences;

namespace Sample.Infrastructure.Persistence.Repositories;

internal sealed class AbsenceRequestRepository(AppDbContext db) : IAbsenceRequestRepository
{
    public Task<AbsenceRequest?> FindVisibleAsync(Guid id, Viewer viewer, CancellationToken cancellationToken) =>
        VisibleTo(viewer).SingleOrDefaultAsync(r => r.Id == id, cancellationToken);

    public async Task<IReadOnlyList<DateRange>> GetActivePeriodsAsync(Guid employeeId, CancellationToken cancellationToken) =>
        await db.AbsenceRequests.AsNoTracking()
            .Where(r => r.EmployeeId == employeeId
                && (r.Status == AbsenceStatus.Submitted || r.Status == AbsenceStatus.Approved))
            .Select(r => r.Period)
            .ToListAsync(cancellationToken);

    public async Task<PagedResult<AbsenceRequest>> ListVisibleAsync(
        AbsenceRequestFilter filter,
        PageRequest page,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(filter);

        var query = VisibleTo(filter.Viewer).AsNoTracking();
        if (filter.Status is { } status)
        {
            query = query.Where(r => r.Status == status);
        }

        if (filter.EmployeeId is { } employeeId)
        {
            query = query.Where(r => r.EmployeeId == employeeId);
        }

        var total = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderByDescending(r => r.SubmittedAt)
            .ThenBy(r => r.Id)
            .Skip(page.Offset)
            .Take(page.PageSize)
            .ToListAsync(cancellationToken);

        return new PagedResult<AbsenceRequest>(items, page.Page, page.PageSize, total);
    }

    public void Add(AbsenceRequest request) => db.AbsenceRequests.Add(request);

    private IQueryable<AbsenceRequest> VisibleTo(Viewer viewer)
    {
        ArgumentNullException.ThrowIfNull(viewer);

        if (!viewer.IsManager)
        {
            return db.AbsenceRequests.Where(r => r.EmployeeId == viewer.EmployeeId);
        }

        var reports = db.Employees.Where(e => e.ManagerId == viewer.EmployeeId).Select(e => e.Id);
        return db.AbsenceRequests.Where(r => r.EmployeeId == viewer.EmployeeId || reports.Contains(r.EmployeeId));
    }
}
