using k8s.Models;
using Preview.Operator.Crds;

namespace Preview.Operator.Tests.TestSupport;

internal static class Previews
{
    public static readonly DateTimeOffset Created = new(2026, 3, 4, 9, 0, 0, TimeSpan.Zero);

    public static PreviewEnvironment ForPullRequest(int number, string ttl = "2h", string tag = "3f2c1a9", bool seed = false) => new()
    {
        Metadata = new V1ObjectMeta
        {
            Name = $"pr-{number}",
            Uid = Guid.NewGuid().ToString(),
            Generation = 1,
            CreationTimestamp = Created.UtcDateTime,
        },
        Spec = new PreviewEnvironmentSpec
        {
            PullRequest = number,
            Image = $"ghcr.io/example/sample-api:{tag}",
            Ttl = ttl,
            SeedData = seed,
        },
    };
}
