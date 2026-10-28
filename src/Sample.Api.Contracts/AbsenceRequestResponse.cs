namespace Sample.Api.Contracts;

public sealed record AbsenceRequestResponse(
    Guid Id,
    Guid EmployeeId,
    AbsenceType Type,
    AbsenceStatus Status,
    DateOnly StartDate,
    DateOnly EndDate,
    int WorkingDays,
    string? Comment,
    DateTimeOffset SubmittedAt,
    DateTimeOffset? DecidedAt,
    Guid? DecidedBy,
    string? RejectionReason);
