namespace Sample.Api.Contracts;

/// <summary>
/// Codes emitted by the HTTP layer in the <c>code</c> extension of problem details. Business codes such as
/// <c>absence.overlapping</c> come from the domain and are listed in <c>docs/api.md</c>.
/// </summary>
public static class ErrorCodes
{
    public const string ValidationFailed = "request.validation_failed";
    public const string IdempotencyKeyMissing = "idempotency.key_missing";
    public const string IdempotencyKeyInvalid = "idempotency.key_invalid";
    public const string IdempotencyKeyReused = "idempotency.key_reused";
    public const string IdempotencyInProgress = "idempotency.in_progress";
    public const string RateLimited = "request.rate_limited";
    public const string FaultInjected = "request.fault_injected";
}
