using Sample.Domain.Employees;

namespace Sample.Application.Abstractions;

public interface IEmployeeRepository
{
    Task<Employee?> FindAsync(Guid id, CancellationToken cancellationToken);
}
