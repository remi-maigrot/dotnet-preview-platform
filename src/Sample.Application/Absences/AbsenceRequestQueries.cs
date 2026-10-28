using Sample.Application.Abstractions;
using Sample.Application.Common;
using Sample.Domain.Absences;
using Sample.Domain.Common;

namespace Sample.Application.Absences;

public sealed class AbsenceRequestQueries(IAbsenceRequestRepository requests)
{
    public async Task<Result<AbsenceRequest>> GetAsync(Guid id, Viewer viewer, CancellationToken cancellationToken)
    {
        var request = await requests.FindVisibleAsync(id, viewer, cancellationToken);
        return request is null ? AbsenceErrors.NotFound : request;
    }

    public Task<PagedResult<AbsenceRequest>> ListAsync(AbsenceRequestFilter filter, PageRequest page, CancellationToken cancellationToken) =>
        requests.ListVisibleAsync(filter, page, cancellationToken);
}
