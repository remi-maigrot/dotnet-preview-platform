namespace Sample.Application.Abstractions;

public interface IUnitOfWork
{
    /// <summary>Persists pending changes.</summary>
    /// <exception cref="ConcurrencyConflictException">Another writer changed the same data in the meantime.</exception>
    Task SaveChangesAsync(CancellationToken cancellationToken);
}

/// <summary>Raised by <see cref="IUnitOfWork"/> when optimistic concurrency detects a lost update.</summary>
public sealed class ConcurrencyConflictException : Exception
{
    public ConcurrencyConflictException()
    {
    }

    public ConcurrencyConflictException(string message)
        : base(message)
    {
    }

    public ConcurrencyConflictException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
