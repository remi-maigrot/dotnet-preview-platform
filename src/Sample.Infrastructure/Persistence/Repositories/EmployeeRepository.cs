using Microsoft.EntityFrameworkCore;
using Sample.Application.Abstractions;
using Sample.Domain.Employees;

namespace Sample.Infrastructure.Persistence.Repositories;

internal sealed class EmployeeRepository(AppDbContext db) : IEmployeeRepository
{
    public Task<Employee?> FindAsync(Guid id, CancellationToken cancellationToken) =>
        db.Employees.SingleOrDefaultAsync(e => e.Id == id, cancellationToken);
}
