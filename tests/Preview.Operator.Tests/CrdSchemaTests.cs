using System.Reflection;
using System.Text.Json.Serialization;
using k8s;
using k8s.Models;
using Preview.Operator.Crds;

namespace Preview.Operator.Tests;

/// <summary>The CRD schema (YAML) and the C# model are written by hand; these tests keep them in sync.</summary>
public sealed class CrdSchemaTests
{
    private static readonly V1CustomResourceDefinition Crd = KubernetesYaml.Deserialize<V1CustomResourceDefinition>(
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "crds", "previewenvironments.platform.remimaigrot.dev.yaml")));

    private static V1JSONSchemaProps Schema => Crd.Spec.Versions.Single().Schema.OpenAPIV3Schema;

    [Fact]
    public void Identity_MatchesTheModel()
    {
        Crd.Spec.Group.ShouldBe(PreviewEnvironment.Group);
        Crd.Spec.Names.Kind.ShouldBe(PreviewEnvironment.KindName);
        Crd.Spec.Names.Plural.ShouldBe(PreviewEnvironment.Plural);
        Crd.Spec.Versions.Single().Name.ShouldBe(PreviewEnvironment.Version);
        Crd.Spec.Scope.ShouldBe("Cluster");
    }

    [Fact]
    public void SpecProperties_MatchTheModel() =>
        Schema.Properties["spec"].Properties.Keys.ShouldBe(JsonNames<PreviewEnvironmentSpec>(), ignoreOrder: true);

    [Fact]
    public void StatusProperties_MatchTheModel() =>
        Schema.Properties["status"].Properties.Keys.ShouldBe(JsonNames<PreviewEnvironmentStatus>(), ignoreOrder: true);

    [Fact]
    public void PhaseEnum_MatchesTheModel() =>
        Schema.Properties["status"].Properties["phase"].EnumProperty.Select(e => e.ToString())
            .ShouldBe(PreviewPhase.All, ignoreOrder: true);

    [Fact]
    public void StatusIsASubresource() => Crd.Spec.Versions.Single().Subresources.Status.ShouldNotBeNull();

    [Fact]
    public void Model_RoundTripsThroughTheKubernetesSerializer()
    {
        var yaml = """
            apiVersion: platform.remimaigrot.dev/v1alpha1
            kind: PreviewEnvironment
            metadata:
              name: pr-42
            spec:
              pullRequest: 42
              image: ghcr.io/example/sample-api:3f2c1a9
              ttl: 2h
              seedData: true
            """;

        var preview = KubernetesYaml.Deserialize<PreviewEnvironment>(yaml);

        preview.Spec.PullRequest.ShouldBe(42);
        preview.Spec.Ttl.ShouldBe("2h");
        preview.Spec.SeedData.ShouldBeTrue();
        KubernetesJson.Serialize(preview).ShouldContain("\"pullRequest\":42");
    }

    private static IEnumerable<string> JsonNames<T>() =>
        typeof(T).GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(p => p.GetCustomAttribute<JsonPropertyNameAttribute>()!.Name);
}
