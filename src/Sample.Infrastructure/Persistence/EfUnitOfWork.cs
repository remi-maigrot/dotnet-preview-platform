using Microsoft.EntityFrameworkCore;
using Sample.Application.Abstractions;

namespace Sample.Infrastructure.Persistence;

internal sealed class EfUnitOfWork(AppDbContext db) : IUnitOfWork
{
    public async Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            throw new ConcurrencyConflictException("The entity was modified concurrently.", ex);
        }
    }
}
