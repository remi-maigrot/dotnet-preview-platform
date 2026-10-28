using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Sample.Infrastructure.Configuration;
using Sample.Infrastructure.Persistence;

namespace Sample.Infrastructure.Retention;

public sealed record RetentionResult(int FreeTextErased, int RequestsDeleted);

/// <summary>
/// Applies the retention policy in two set-based statements. Both are idempotent, so running on every replica at
/// the same time is harmless, and a run interrupted halfway is completed by the next one.
/// </summary>
public sealed partial class DataRetentionPurger(AppDbContext db, IOptions<DataRetentionOptions> options, ILogger<DataRetentionPurger> logger)
{
    public async Task<RetentionResult> RunAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        var policy = options.Value;
        var decidedBefore = now - policy.FreeTextRetention;
        var endedBefore = DateOnly.FromDateTime((now - policy.RequestRetention).UtcDateTime);

        // Pending requests keep their comment: someone still has to read it to decide.
        var erased = await db.AbsenceRequests
            .Where(r => r.DecidedAt != null && r.DecidedAt < decidedBefore && (r.Comment != null || r.RejectionReason != null))
            .ExecuteUpdateAsync(set => set
                .SetProperty(r => r.Comment, (string?)null)
                .SetProperty(r => r.RejectionReason, (string?)null), cancellationToken);

        var deleted = await db.AbsenceRequests
            .Where(r => r.Period.End < endedBefore)
            .ExecuteDeleteAsync(cancellationToken);

        LogApplied(erased, deleted, decidedBefore, endedBefore);
        return new RetentionResult(erased, deleted);
    }

    [LoggerMessage(2100, LogLevel.Information,
        "Retention applied: free text erased on {Erased} requests decided before {DecidedBefore:O}, {Deleted} requests ended before {EndedBefore} deleted")]
    private partial void LogApplied(int erased, int deleted, DateTimeOffset decidedBefore, DateOnly endedBefore);
}
