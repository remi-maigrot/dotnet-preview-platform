namespace Sample.Api.Contracts;

public static class ApiHeaders
{
    /// <summary>Required on creation. Replaying the same key with the same body returns the original response.</summary>
    public const string IdempotencyKey = "Idempotency-Key";

    /// <summary>Set to <c>true</c> on a response replayed from the idempotency store.</summary>
    public const string IdempotentReplayed = "Idempotent-Replayed";
}
