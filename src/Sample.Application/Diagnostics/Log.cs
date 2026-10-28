using Microsoft.Extensions.Logging;
using Sample.Domain.Absences;

namespace Sample.Application.Diagnostics;

/// <summary>Source-generated log messages. Event ids are stable so they can be queried in Loki.</summary>
internal static partial class Log
{
    [LoggerMessage(1000, LogLevel.Information, "Absence request {RequestId} submitted by {EmployeeId} ({AbsenceType}, {WorkingDays} working days)")]
    public static partial void AbsenceSubmitted(this ILogger logger, Guid requestId, Guid employeeId, AbsenceType absenceType, int workingDays);

    [LoggerMessage(1001, LogLevel.Information, "Absence request {RequestId} is now {Status}")]
    public static partial void AbsenceStatusChanged(this ILogger logger, Guid requestId, AbsenceStatus status);

    [LoggerMessage(1002, LogLevel.Warning, "Absence request {RequestId} refused: {ErrorCode}")]
    public static partial void AbsenceOperationRefused(this ILogger logger, Guid requestId, string errorCode);

    [LoggerMessage(1003, LogLevel.Warning, "Concurrent update detected on absence request {RequestId}")]
    public static partial void ConcurrentUpdate(this ILogger logger, Guid requestId);
}
