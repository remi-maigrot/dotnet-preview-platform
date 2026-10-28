namespace Sample.Application.Abstractions;

/// <summary>Stores the outcome of write requests carrying an <c>Idempotency-Key</c> header.</summary>
public interface IIdempotencyStore
{
    /// <summary>
    /// Reserves the key for this caller. Returns <c>null</c> when the reservation succeeded,
    /// otherwise the record that already holds the key.
    /// </summary>
    Task<IdempotencyRecord?> TryReserveAsync(string key, Guid callerId, string requestHash, CancellationToken cancellationToken);

    Task CompleteAsync(string key, Guid callerId, StoredResponse response, CancellationToken cancellationToken);

    /// <summary>Frees the key so the client can retry, used when the request failed unexpectedly.</summary>
    Task ReleaseAsync(string key, Guid callerId, CancellationToken cancellationToken);

    Task<int> PurgeExpiredAsync(DateTimeOffset olderThan, CancellationToken cancellationToken);
}

public sealed record IdempotencyRecord(string RequestHash, StoredResponse? Response);

public sealed record StoredResponse(int StatusCode, string? Location, string? Body);
