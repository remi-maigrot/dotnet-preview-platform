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

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        TruncateTimestampsToStoredPrecision();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        TruncateTimestampsToStoredPrecision();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);

    // PostgreSQL stores microseconds, .NET clocks tick every 100 ns on Linux. Truncating the entity itself, not
    // only the stored value, keeps what the API returns after a write equal to what it reads back later.
    private void TruncateTimestampsToStoredPrecision()
    {
        foreach (var entry in ChangeTracker.Entries())
        {
            if (entry.State is not (EntityState.Added or EntityState.Modified))
            {
                continue;
            }

            foreach (var property in entry.Properties)
            {
                if (property.CurrentValue is DateTimeOffset value && value.Ticks % TimeSpan.TicksPerMicrosecond != 0)
                {
                    property.CurrentValue = value.AddTicks(-(value.Ticks % TimeSpan.TicksPerMicrosecond));
                }
            }
        }
    }
}
