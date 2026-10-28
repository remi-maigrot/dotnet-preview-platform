using Sample.Domain.Common;

namespace Sample.Application.Absences;

public static class EmployeeErrors
{
    public static readonly DomainError Unknown =
        DomainError.Forbidden("employee.unknown", "The authenticated user is not a known employee.");
}
