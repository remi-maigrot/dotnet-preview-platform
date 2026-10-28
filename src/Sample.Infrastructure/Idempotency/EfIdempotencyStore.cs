using Microsoft.EntityFrameworkCore;
using Sample.Application.Abstractions;
using Sample.Infrastructure.Persistence;

namespace Sample.Infrastructure.Idempotency;

/// <summary>
/// PostgreSQL-backed idempotency store. The reservation is an <c>INSERT … ON CONFLICT DO NOTHING</c>, so two
/// concurrent requests with the same key cannot both run: the database arbitrates, not the application.
/// </summary>
internal sealed class EfIdempotencyStore(AppDbContext db, TimeProvider clock) : IIdempotencyStore
{
    public async Task<IdempotencyRecord?> TryReserveAsync(string key, Guid callerId, string requestHash, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        var inserted = await db.Database.ExecuteSqlInterpolatedAsync(
            $"""
            INSERT INTO idempotency_entries (key, caller_id, request_hash, created_at)
            VALUES ({key}, {callerId}, {requestHash}, {now})
            ON CONFLICT (caller_id, key) DO NOTHING
            """,
            cancellationToken);
        if (inserted == 1)
        {
            return null;
        }

        var existing = await db.IdempotencyEntries.AsNoTracking()
            .SingleAsync(e => e.CallerId == callerId && e.Key == key, cancellationToken);
        var response = existing.StatusCode is { } status
            ? new StoredResponse(status, existing.Location, existing.ResponseBody)
            : null;
        return new IdempotencyRecord(existing.RequestHash, response);
    }

    public Task CompleteAsync(string key, Guid callerId, StoredResponse response, CancellationToken cancellationToken) =>
        db.IdempotencyEntries
            .Where(e => e.CallerId == callerId && e.Key == key)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(e => e.StatusCode, response.StatusCode)
                    .SetProperty(e => e.Location, response.Location)
                    .SetProperty(e => e.ResponseBody, response.Body),
                cancellationToken);

    public Task ReleaseAsync(string key, Guid callerId, CancellationToken cancellationToken) =>
        db.IdempotencyEntries
            .Where(e => e.CallerId == callerId && e.Key == key && e.StatusCode == null)
            .ExecuteDeleteAsync(cancellationToken);

    public Task<int> PurgeExpiredAsync(DateTimeOffset olderThan, CancellationToken cancellationToken) =>
        db.IdempotencyEntries.Where(e => e.CreatedAt < olderThan).ExecuteDeleteAsync(cancellationToken);
}
