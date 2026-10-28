using Microsoft.EntityFrameworkCore;
using Sample.Infrastructure.Persistence;

namespace Preview.DataSeeder;

/// <summary>Reads and writes the raw rows. Runs after the migrations, as the schema owner.</summary>
internal sealed class DatasetStore(AppDbContext db)
{
    public Task<bool> IsSeededAsync(CancellationToken cancellationToken) =>
        db.Employees.AnyAsync(e => e.Id == KnownAccounts.Manager, cancellationToken);

    public async Task<Dataset> ReadAsync(CancellationToken cancellationToken)
    {
        // The snake_case naming convention also applies to these unmapped row types: columns are matched as-is.
        var employees = await db.Database.SqlQueryRaw<EmployeeRow>(
                "SELECT id, full_name, email, manager_id FROM employees")
            .ToListAsync(cancellationToken);
        var requests = await db.Database.SqlQueryRaw<AbsenceRequestRow>(
                """
                SELECT id, employee_id, type, status, start_date, end_date, working_days, comment,
                       submitted_at, decided_at, decided_by, rejection_reason
                FROM absence_requests
                """)
            .ToListAsync(cancellationToken);
        return new Dataset(employees, requests);
    }

    public async Task WriteAsync(Dataset dataset, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(dataset);

        var strategy = db.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

            // Managers first: the self-reference on manager_id must point at an existing row.
            foreach (var e in dataset.Employees.OrderBy(e => e.ManagerId is not null))
            {
                await db.Database.ExecuteSqlInterpolatedAsync(
                    $"INSERT INTO employees (id, full_name, email, manager_id) VALUES ({e.Id}, {e.FullName}, {e.Email}, {e.ManagerId})",
                    cancellationToken);
            }

            foreach (var r in dataset.AbsenceRequests)
            {
                await db.Database.ExecuteSqlInterpolatedAsync(
                    $"""
                    INSERT INTO absence_requests (id, employee_id, type, status, start_date, end_date, working_days, comment,
                                                  submitted_at, decided_at, decided_by, rejection_reason)
                    VALUES ({r.Id}, {r.EmployeeId}, {r.Type}, {r.Status}, {r.StartDate}, {r.EndDate}, {r.WorkingDays}, {r.Comment},
                            {r.SubmittedAt}, {r.DecidedAt}, {r.DecidedBy}, {r.RejectionReason})
                    """,
                    cancellationToken);
            }

            await transaction.CommitAsync(cancellationToken);
        });
    }
}
