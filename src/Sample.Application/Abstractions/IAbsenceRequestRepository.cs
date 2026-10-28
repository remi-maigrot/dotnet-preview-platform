using Sample.Application.Absences;
using Sample.Application.Common;
using Sample.Domain.Absences;

namespace Sample.Application.Abstractions;

public interface IAbsenceRequestRepository
{
    /// <summary>Finds a request visible to <paramref name="viewer"/>; invisible requests are reported as missing.</summary>
    Task<AbsenceRequest?> FindVisibleAsync(Guid id, Viewer viewer, CancellationToken cancellationToken);

    /// <summary>Periods of the employee's requests that still block the calendar (submitted or approved).</summary>
    Task<IReadOnlyList<DateRange>> GetActivePeriodsAsync(Guid employeeId, CancellationToken cancellationToken);

    Task<PagedResult<AbsenceRequest>> ListVisibleAsync(AbsenceRequestFilter filter, PageRequest page, CancellationToken cancellationToken);

    void Add(AbsenceRequest request);
}
