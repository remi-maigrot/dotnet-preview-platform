using Devctl.Cli.Diagnostics;

namespace Devctl.Cli.Tests;

/// <summary>What leaves the machine when an assistant is asked to explain a failure: secrets and people must not.</summary>
public sealed class RedactorTests
{
    [Theory]
    [InlineData("Host=postgresql;Database=absences;Username=app;Password=s3cr3t-Pa55;Timeout=5", "s3cr3t-Pa55")]
    [InlineData("connection failed: User ID=app;Pwd=hunter2;Server=db", "hunter2")]
    [InlineData("postgresql://app:hunter2@db.local:5432/absences", "hunter2")]
    [InlineData("Authorization: Bearer abc.def.ghi-token", "abc.def.ghi-token")]
    [InlineData("token eyJhbGciOiJIUzI1NiJ9.eyJzdWIiOiIxIn0.c2lnbmF0dXJlLWJ5dGVz rejected", "eyJzdWIiOiIxIn0")]
    [InlineData("Authentication__Jwt__SigningKey=0123456789abcdef0123456789abcdef", "0123456789abcdef0123456789abcdef")]
    [InlineData("\"POSTGRES_PASSWORD\": \"x9Y8z7W6\"", "x9Y8z7W6")]
    [InlineData("api_key: sk-live-0000111122223333", "sk-live-0000111122223333")]
    [InlineData("AGE-SECRET-KEY-1QYQSZQGPQYQSZQGPQYQSZQGPQYQSZQGPQYQSZQGPQYQSZQGPQYQS0Q3XNG", "AGE-SECRET-KEY-1QYQS")]
    public void Secrets_AreRemoved(string input, string secret)
    {
        var redacted = Redactor.Redact(input);

        redacted.ShouldNotContain(secret);
        redacted.ShouldContain(Redactor.Placeholder);
    }

    [Fact]
    public void Emails_AreRemoved() =>
        Redactor.Redact("submitted by alice.martin@corp.example for bob@example.test")
            .ShouldBe("submitted by <email> for <email>");

    [Theory]
    [InlineData("Back-off pulling image \"ghcr.io/owner/sample-api:abc123\"")]
    [InlineData("0/2 nodes are available: 2 Insufficient memory.")]
    [InlineData("exceeded quota: preview-quota, requested: limits.memory=512Mi, used: limits.memory=1Gi")]
    [InlineData("Readiness probe failed: HTTP probe failed with statuscode: 503")]
    [InlineData("job sample-api-deploy-1a2b3c failed: BackoffLimitExceeded")]
    public void TheDiagnosis_IsLeftIntact(string input) => Redactor.Redact(input).ShouldBe(input);
}
