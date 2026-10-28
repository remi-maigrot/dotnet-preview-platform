namespace Preview.DataSeeder;

/// <summary>Raw rows of the tables being copied. Property names match the domain so the PersonalData test can map them.</summary>
internal sealed record EmployeeRow(Guid Id, string FullName, string Email, Guid? ManagerId);

internal sealed record AbsenceRequestRow(
    Guid Id,
    Guid EmployeeId,
    string Type,
    string Status,
    DateOnly StartDate,
    DateOnly EndDate,
    int WorkingDays,
    string? Comment,
    DateTimeOffset SubmittedAt,
    DateTimeOffset? DecidedAt,
    Guid? DecidedBy,
    string? RejectionReason);

internal sealed record Dataset(IReadOnlyList<EmployeeRow> Employees, IReadOnlyList<AbsenceRequestRow> AbsenceRequests);
