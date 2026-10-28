using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Sample.Application.Abstractions;
using Sample.Application.Configuration;
using Sample.Application.Diagnostics;
using Sample.Domain.Absences;
using Sample.Domain.Common;

namespace Sample.Application.Absences;

public sealed record SubmitAbsenceRequestCommand(
    Guid EmployeeId,
    AbsenceType Type,
    DateOnly Start,
    DateOnly End,
    string? Comment);

public sealed class SubmitAbsenceRequestHandler(
    IEmployeeRepository employees,
    IAbsenceRequestRepository requests,
    IPublicHolidayProvider holidays,
    IUnitOfWork unitOfWork,
    IOptionsSnapshot<FeatureFlagsOptions> features,
    AbsenceMetrics metrics,
    TimeProvider clock,
    ILogger<SubmitAbsenceRequestHandler> logger)
{
    public async Task<Result<AbsenceRequest>> HandleAsync(SubmitAbsenceRequestCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var period = DateRange.Create(command.Start, command.End);
        if (!period.IsSuccess)
        {
            return period.Error;
        }

        var employee = await employees.FindAsync(command.EmployeeId, cancellationToken);
        if (employee is null)
        {
            return EmployeeErrors.Unknown;
        }

        var publicHolidays = await holidays.GetHolidaysAsync(period.Value, cancellationToken);
        var activePeriods = await requests.GetActivePeriodsAsync(employee.Id, cancellationToken);
        var now = clock.GetUtcNow();

        var submitted = AbsenceRequest.Submit(
            Guid.CreateVersion7(now),
            employee,
            command.Type,
            period.Value,
            command.Comment,
            publicHolidays,
            activePeriods,
            now);
        if (!submitted.IsSuccess)
        {
            return submitted;
        }

        var request = submitted.Value;
        var autoApproved = request.Type == AbsenceType.SickLeave && features.Value.AutoApproveSickLeave;
        if (autoApproved)
        {
            request.ApproveAutomatically(now);
        }

        requests.Add(request);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        metrics.Submitted(request.Type);
        logger.AbsenceSubmitted(request.Id, employee.Id, request.Type, request.WorkingDays);
        if (autoApproved)
        {
            metrics.Decided(AbsenceStatus.Approved, automatic: true);
            logger.AbsenceStatusChanged(request.Id, request.Status);
        }

        return request;
    }
}
