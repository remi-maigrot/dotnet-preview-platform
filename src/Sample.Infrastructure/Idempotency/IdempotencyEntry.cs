namespace Sample.Infrastructure.Idempotency;

internal sealed class IdempotencyEntry
{
    public required string Key { get; init; }

    public required Guid CallerId { get; init; }

    public required string RequestHash { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }

    public int? StatusCode { get; set; }

    public string? Location { get; set; }

    public string? ResponseBody { get; set; }
}
