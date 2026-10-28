using Preview.Operator.Reconciliation;

namespace Preview.Operator.Tests;

public sealed class ParsingTests
{
    [Theory]
    [InlineData("2h", 120)]
    [InlineData("90m", 90)]
    [InlineData("1h30m", 90)]
    public void Ttl_ParsesGoDurations(string value, int minutes)
    {
        Ttl.TryParse(value, out var ttl).ShouldBeTrue();
        ttl.ShouldBe(TimeSpan.FromMinutes(minutes));
    }

    [Theory]
    [InlineData("")]
    [InlineData("0h")]
    [InlineData("2d")]
    [InlineData("1.5h")]
    [InlineData(null)]
    public void Ttl_RejectsAnythingElse(string? value) => Ttl.TryParse(value, out _).ShouldBeFalse();

    [Theory]
    [InlineData("ghcr.io/org/sample-api:3f2c1a9", "ghcr.io/org/sample-api", "3f2c1a9")]
    [InlineData("k3d-registry.localhost:5000/sample-api:dev", "k3d-registry.localhost:5000/sample-api", "dev")]
    [InlineData("sample-api:local", "sample-api", "local")]
    public void Image_SplitsRepositoryAndTag(string value, string repository, string tag)
    {
        ImageReference.TryParse(value, out var image).ShouldBeTrue();

        image.Repository.ShouldBe(repository);
        image.Tag.ShouldBe(tag);
        image.ToString().ShouldBe(value);
    }

    [Fact]
    public void Image_WithDigest_KeepsTheDigest()
    {
        ImageReference.TryParse("ghcr.io/org/api@sha256:" + new string('a', 64), out var image).ShouldBeTrue();

        image.Tag.ShouldBeNull();
        image.Digest.ShouldStartWith("sha256:");
    }

    [Theory]
    [InlineData("ghcr.io/org/sample-api")]
    [InlineData("k3d-registry.localhost:5000/sample-api")]
    [InlineData("ghcr.io/org/api:")]
    [InlineData("has space:tag")]
    public void Image_WithoutTag_IsRejected(string value) => ImageReference.TryParse(value, out _).ShouldBeFalse();

    [Fact]
    public void Image_Sibling_KeepsRegistryAndTag()
    {
        ImageReference.TryParse("k3d-registry.localhost:5000/team/sample-api:abc", out var image).ShouldBeTrue();

        image.Sibling("sample-migrator").ToString().ShouldBe("k3d-registry.localhost:5000/team/sample-migrator:abc");
    }
}
