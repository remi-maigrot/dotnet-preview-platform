using Microsoft.Extensions.Logging;
using Sample.Application.Abstractions;
using Sample.Application.Common;
using Sample.Application.Diagnostics;
using Sample.Domain.Absences;
using Sample.Domain.Common;
using Sample.Domain.Employees;

namespace Sample.Application.Absences;

/// <summary>Approve, reject and cancel: every transition that changes an existing request.</summary>
public sealed class ChangeAbsenceRequestHandler(
    IEmployeeRepository employees,
    IAbsenceRequestRepository requests,
    IUnitOfWork unitOfWork,
    AbsenceMetrics metrics,
    TimeProvider clock,
    ILogger<ChangeAbsenceRequestHandler> logger)
{
    public Task<Result<AbsenceRequest>> ApproveAsync(Guid requestId, Viewer manager, CancellationToken cancellationToken) =>
        ChangeAsync(requestId, manager, (request, owner, now) => request.Approve(owner, manager.EmployeeId, now), cancellationToken);

    public Task<Result<AbsenceRequest>> RejectAsync(Guid requestId, Viewer manager, string reason, CancellationToken cancellationToken) =>
        ChangeAsync(requestId, manager, (request, owner, now) => request.Reject(owner, manager.EmployeeId, reason, now), cancellationToken);

    public Task<Result<AbsenceRequest>> CancelAsync(Guid requestId, Viewer requester, CancellationToken cancellationToken) =>
        ChangeAsync(requestId, requester, (request, _, now) => request.Cancel(requester.EmployeeId, now), cancellationToken);

    private async Task<Result<AbsenceRequest>> ChangeAsync(
        Guid requestId,
        Viewer viewer,
        Func<AbsenceRequest, Employee, DateTimeOffset, Result> transition,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(viewer);

        var request = await requests.FindVisibleAsync(requestId, viewer, cancellationToken);
        if (request is null)
        {
            return AbsenceErrors.NotFound;
        }

        var owner = await employees.FindAsync(request.EmployeeId, cancellationToken)
            ?? throw new InvalidOperationException($"Absence request {request.Id} references a missing employee.");

        var outcome = transition(request, owner, clock.GetUtcNow());
        if (!outcome.IsSuccess)
        {
            logger.AbsenceOperationRefused(request.Id, outcome.Error.Code);
            return outcome.Error;
        }

        try
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (ConcurrencyConflictException)
        {
            logger.ConcurrentUpdate(request.Id);
            return AbsenceErrors.ConcurrentUpdate;
        }

        metrics.Decided(request.Status, automatic: false);
        logger.AbsenceStatusChanged(request.Id, request.Status);
        return request;
    }
}
