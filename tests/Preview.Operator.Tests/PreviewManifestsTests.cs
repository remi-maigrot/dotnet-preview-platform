using k8s.Models;
using Preview.Operator.Reconciliation;
using Preview.Operator.Tests.TestSupport;

namespace Preview.Operator.Tests;

public sealed class PreviewManifestsTests
{
    private readonly OperatorOptions _options = new() { DeployerImage = "ghcr.io/example/sample-deployer:3f2c1a9" };

    [Fact]
    public void Namespace_IsRestrictedAndOwnedByThePreview()
    {
        var preview = Previews.ForPullRequest(7);

        var ns = Manifests(preview).PreviewNamespace();

        ns.Metadata.Name.ShouldBe("pr-7");
        ns.Metadata.Labels["pod-security.kubernetes.io/enforce"].ShouldBe("restricted");
        var owner = ns.Metadata.OwnerReferences.ShouldHaveSingleItem();
        owner.Uid.ShouldBe(preview.Metadata.Uid);
        owner.Controller.ShouldBe(true);
    }

    [Fact]
    public void EveryPodTemplate_MeetsTheRestrictedPodSecurityStandard()
    {
        var manifests = Manifests(Previews.ForPullRequest(7, seed: true), seeder: true);
        var templates = new[]
        {
            manifests.DatabaseStatefulSet().Spec.Template.Spec,
            manifests.DeployJob().Spec.Template.Spec,
            manifests.SeedJob().Spec.Template.Spec,
        };

        foreach (var pod in templates)
        {
            pod.SecurityContext.RunAsNonRoot.ShouldBe(true);
            pod.SecurityContext.SeccompProfile.Type.ShouldBe("RuntimeDefault");
            foreach (var container in pod.Containers)
            {
                container.SecurityContext.AllowPrivilegeEscalation.ShouldBe(false);
                container.SecurityContext.ReadOnlyRootFilesystem.ShouldBe(true);
                container.SecurityContext.Capabilities.Drop.ShouldBe(["ALL"]);
                container.Resources.Limits.ShouldContainKey("memory");
            }
        }
    }

    [Fact]
    public void OperatorPermissions_AreGrantedRightAfterTheNamespace()
    {
        var resources = Manifests(Previews.ForPullRequest(7)).Resources();

        var binding = resources[1].ShouldBeOfType<V1RoleBinding>();
        binding.RoleRef.Name.ShouldBe(_options.NamespaceClusterRole);
        binding.Subjects.ShouldHaveSingleItem().NamespaceProperty.ShouldBe(_options.Namespace);
    }

    [Fact]
    public void Network_IsDeniedByDefault()
    {
        var deny = Manifests(Previews.ForPullRequest(7)).DenyAll();

        deny.Spec.PodSelector.MatchLabels.ShouldBeNull();
        deny.Spec.PolicyTypes.ShouldBe(["Ingress", "Egress"]);
        deny.Spec.Ingress.ShouldBeNull();
        deny.Spec.Egress.ShouldBeNull();
    }

    [Fact]
    public void Resources_AllLiveInThePreviewNamespace()
    {
        var manifests = Manifests(Previews.ForPullRequest(7));

        manifests.Resources().Skip(1).ShouldAllBe(r => r.Metadata.NamespaceProperty == "pr-7");
        manifests.Resources().Select(r => r.Kind).ShouldContain("ResourceQuota");
        manifests.Resources().Select(r => r.Kind).ShouldContain("LimitRange");
    }

    [Fact]
    public void Secrets_GiveTheApiADataOnlyRoleAndTheMigratorTheOwner()
    {
        var secrets = Manifests(Previews.ForPullRequest(7)).Secrets("owner-pw", "app-pw", "signing-key");

        var api = secrets.Single(s => s.Metadata.Name == PreviewManifests.ApiSecret).StringData;
        api["Database__ConnectionString"].ShouldContain("Username=app;Password=app-pw");
        api["Authentication__Jwt__SigningKey"].ShouldBe("signing-key");
        secrets.Single(s => s.Metadata.Name == PreviewManifests.MigratorSecret).StringData["Database__ConnectionString"]
            .ShouldContain("Username=postgres;Password=owner-pw");
    }

    [Fact]
    public void HelmValues_PointAtThePreviewHostAndImages()
    {
        var values = Manifests(Previews.ForPullRequest(7, tag: "abc1234")).HelmValues();

        values.ShouldContain("repository: ghcr.io/example/sample-api");
        values.ShouldContain("tag: \"abc1234\"");
        values.ShouldContain("repository: ghcr.io/example/sample-migrator");
        values.ShouldContain("host: pr-7.preview.localtest.me");
        values.ShouldContain("replicaCount: 1");
    }

    [Fact]
    public void DeploymentHash_IsStableAndFollowsTheImage()
    {
        var first = Manifests(Previews.ForPullRequest(7, tag: "abc1234")).DeployJobName;

        Manifests(Previews.ForPullRequest(7, tag: "abc1234")).DeployJobName.ShouldBe(first);
        Manifests(Previews.ForPullRequest(7, tag: "def5678")).DeployJobName.ShouldNotBe(first);
    }

    [Fact]
    public void Url_IncludesThePublicPortWhenConfigured()
    {
        _options.PublicPort = 8080;

        Manifests(Previews.ForPullRequest(7)).Url.ShouldBe("http://pr-7.preview.localtest.me:8080");
    }

    [Fact]
    public void LocalEnvironment_HasItsOwnNamespaceAndHost()
    {
        var manifests = Manifests(Previews.ForPullRequest(0));

        manifests.Namespace.ShouldBe("local");
        manifests.Host.ShouldBe("local.preview.localtest.me");
    }

    [Fact]
    public void SeedJob_WithoutSeederImage_Throws() =>
        Should.Throw<InvalidOperationException>(() => Manifests(Previews.ForPullRequest(7)).SeedJob());

    private PreviewManifests Manifests(Crds.PreviewEnvironment preview, bool seeder = false)
    {
        if (seeder)
        {
            _options.SeederImage = "ghcr.io/example/sample-seeder:3f2c1a9";
        }

        ImageReference.TryParse(preview.Spec.Image, out var image).ShouldBeTrue();
        return new PreviewManifests(preview, _options, image);
    }
}
