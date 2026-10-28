using Sample.Domain.Common;
using Sample.Domain.Employees;

namespace Sample.Domain.Absences;

/// <summary>
/// An employee's request to be away for a period. State machine:
/// <c>Submitted → Approved | Rejected | Cancelled</c>, and <c>Approved → Cancelled</c> while the absence has not started.
/// </summary>
public sealed class AbsenceRequest
{
    // Required by EF Core to materialise the aggregate.
    private AbsenceRequest()
    {
    }

    public Guid Id { get; private set; }

    public Guid EmployeeId { get; private set; }

    public AbsenceType Type { get; private set; }

    public DateRange Period { get; private set; }

    public int WorkingDays { get; private set; }

    /// <summary>Free text: people write names, health details or phone numbers here, so it is treated as personal data.</summary>
    [PersonalData]
    public string? Comment { get; private set; }

    public AbsenceStatus Status { get; private set; }

    public DateTimeOffset SubmittedAt { get; private set; }

    public DateTimeOffset? DecidedAt { get; private set; }

    public Guid? DecidedBy { get; private set; }

    [PersonalData]
    public string? RejectionReason { get; private set; }

    public static Result<AbsenceRequest> Submit(
        Guid id,
        Employee employee,
        AbsenceType type,
        DateRange period,
        string? comment,
        IReadOnlySet<DateOnly> publicHolidays,
        IEnumerable<DateRange> activePeriodsOfEmployee,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(employee);
        ArgumentNullException.ThrowIfNull(activePeriodsOfEmployee);

        // Sick leave is declared after the fact, every other absence is planned ahead.
        if (type != AbsenceType.SickLeave && period.Start < DateOnly.FromDateTime(now.UtcDateTime))
        {
            return AbsenceErrors.StartsInThePast;
        }

        if (period.CalendarDays > AbsenceErrors.MaxCalendarDays)
        {
            return AbsenceErrors.TooLong;
        }

        if (comment?.Length > AbsenceErrors.MaxCommentLength)
        {
            return AbsenceErrors.CommentTooLong;
        }

        var workingDays = Absences.WorkingDays.Count(period, publicHolidays);
        if (workingDays == 0)
        {
            return AbsenceErrors.NoWorkingDay;
        }

        if (activePeriodsOfEmployee.Any(period.Overlaps))
        {
            return AbsenceErrors.Overlapping;
        }

        return new AbsenceRequest
        {
            Id = id,
            EmployeeId = employee.Id,
            Type = type,
            Period = period,
            WorkingDays = workingDays,
            Comment = string.IsNullOrWhiteSpace(comment) ? null : comment.Trim(),
            Status = AbsenceStatus.Submitted,
            SubmittedAt = now,
        };
    }

    public Result Approve(Employee employee, Guid managerId, DateTimeOffset now)
    {
        var check = EnsureCanDecide(employee, managerId);
        if (!check.IsSuccess)
        {
            return check;
        }

        Status = AbsenceStatus.Approved;
        DecidedAt = now;
        DecidedBy = managerId;
        return Result.Success();
    }

    public Result Reject(Employee employee, Guid managerId, string reason, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            return AbsenceErrors.RejectionReasonRequired;
        }

        if (reason.Length > AbsenceErrors.MaxCommentLength)
        {
            return AbsenceErrors.CommentTooLong;
        }

        var check = EnsureCanDecide(employee, managerId);
        if (!check.IsSuccess)
        {
            return check;
        }

        Status = AbsenceStatus.Rejected;
        DecidedAt = now;
        DecidedBy = managerId;
        RejectionReason = reason.Trim();
        return Result.Success();
    }

    /// <summary>Approves without a manager decision (used when the auto-approval feature flag is on).</summary>
    public Result ApproveAutomatically(DateTimeOffset now)
    {
        if (Status != AbsenceStatus.Submitted)
        {
            return AbsenceErrors.NotPending;
        }

        Status = AbsenceStatus.Approved;
        DecidedAt = now;
        return Result.Success();
    }

    public Result Cancel(Guid requesterId, DateTimeOffset now)
    {
        if (requesterId != EmployeeId)
        {
            return AbsenceErrors.NotTheOwner;
        }

        var today = DateOnly.FromDateTime(now.UtcDateTime);
        var cancellable = Status == AbsenceStatus.Submitted
            || (Status == AbsenceStatus.Approved && Period.Start > today);
        if (!cancellable)
        {
            return AbsenceErrors.CannotBeCancelled;
        }

        Status = AbsenceStatus.Cancelled;
        DecidedAt = now;
        return Result.Success();
    }

    private Result EnsureCanDecide(Employee employee, Guid managerId)
    {
        if (employee.Id != EmployeeId)
        {
            throw new ArgumentException("The employee does not own this request.", nameof(employee));
        }

        if (managerId == EmployeeId)
        {
            return AbsenceErrors.SelfApproval;
        }

        if (employee.ManagerId != managerId)
        {
            return AbsenceErrors.NotTheManager;
        }

        return Status == AbsenceStatus.Submitted ? Result.Success() : AbsenceErrors.NotPending;
    }
}
