using Microsoft.EntityFrameworkCore;
using Sample.Domain.Absences;
using Sample.Domain.Employees;
using Sample.Infrastructure.Idempotency;

namespace Sample.Infrastructure.Persistence;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Employee> Employees => Set<Employee>();

    public DbSet<AbsenceRequest> AbsenceRequests => Set<AbsenceRequest>();

    internal DbSet<IdempotencyEntry> IdempotencyEntries => Set<IdempotencyEntry>();

    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
}
