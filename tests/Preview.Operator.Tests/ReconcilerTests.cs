using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Preview.Operator.Crds;
using Preview.Operator.Reconciliation;
using Preview.Operator.Tests.TestSupport;

namespace Preview.Operator.Tests;

public sealed class ReconcilerTests
{
    private const string Ns = "pr-42";

    private readonly FakeClusterGateway _cluster = new();
    private readonly FakeTimeProvider _clock = new(Previews.Created.AddMinutes(1));
    private readonly OperatorOptions _options = new()
    {
        DeployerImage = "ghcr.io/example/sample-deployer:3f2c1a9",
        SeederImage = "ghcr.io/example/sample-seeder:3f2c1a9",
        ResyncPeriod = TimeSpan.FromMinutes(5),
    };

    private readonly PreviewReconciler _reconciler;

    public ReconcilerTests() => _reconciler = new PreviewReconciler(
        _cluster, new SequentialSecretGenerator(), Options.Create(_options), _clock, NullLogger<PreviewReconciler>.Instance);

    [Fact]
    public async Task NewPreview_ProvisionsNamespaceDatabaseAndSecrets()
    {
        var preview = Previews.ForPullRequest(42);

        var result = await ReconcileAsync(preview);

        preview.Metadata.Finalizers.ShouldContain(PreviewReconciler.Finalizer);
        _cluster.Get<k8s.Models.V1Namespace>("Namespace", null, Ns).ShouldNotBeNull();
        _cluster.Get<k8s.Models.V1StatefulSet>("StatefulSet", Ns, PreviewManifests.DatabaseName).ShouldNotBeNull();
        _cluster.SecretsCreated.ShouldBe(1, "only the credentials are created once, derived secrets are applied");
        _cluster.Get<k8s.Models.V1Secret>("Secret", Ns, PreviewManifests.ApiSecret).ShouldNotBeNull();
        _cluster.Jobs(Ns).ShouldBeEmpty("nothing is deployed before the database is ready");

        preview.Status!.Phase.ShouldBe(PreviewPhase.Provisioning);
        preview.Status.Url.ShouldBe("http://pr-42.preview.localtest.me");
        preview.Status.ExpiresAt.ShouldBe(Previews.Created.AddHours(2));
        Condition(preview, PreviewConditions.DatabaseReady).Status.ShouldBe("False");
        result.RequeueAfter.ShouldBe(PreviewReconciler.ProvisioningPollInterval);
        _cluster.Events.ShouldHaveSingleItem().Reason.ShouldBe(PreviewPhase.Provisioning);
    }

    [Fact]
    public async Task Reconcile_Twice_IsIdempotent()
    {
        var preview = Previews.ForPullRequest(42);
        await ReconcileAsync(preview);
        var firstPass = _cluster.ApplyLog.ToList();
        var password = Secret(PreviewManifests.DatabaseCredentialsSecret, "POSTGRES_PASSWORD");

        await ReconcileAsync(preview);

        _cluster.ApplyLog.Skip(firstPass.Count).ShouldBe(firstPass);
        _cluster.SecretsCreated.ShouldBe(1, "generated credentials are never regenerated");
        Secret(PreviewManifests.DatabaseCredentialsSecret, "POSTGRES_PASSWORD").ShouldBe(password);
        _cluster.Events.Count.ShouldBe(1, "no event when the phase does not change");
    }

    [Fact]
    public async Task DatabaseReady_StartsTheDeploymentJob()
    {
        var preview = Previews.ForPullRequest(42);
        await ReconcileAsync(preview);
        _cluster.MarkDatabaseReady(Ns);

        await ReconcileAsync(preview);

        var job = _cluster.Jobs(Ns).ShouldHaveSingleItem();
        job.Metadata.Name.ShouldStartWith("deploy-");
        job.Spec.Template.Spec.Containers[0].Args.ShouldContain("upgrade");
        Condition(preview, PreviewConditions.ApplicationDeployed).Reason.ShouldBe("InProgress");
    }

    [Fact]
    public async Task DeployedAndAvailable_IsReadyAndRequeuedForExpiryOrResync()
    {
        var preview = await DeployAsync(Previews.ForPullRequest(42));
        _cluster.MarkApiAvailable(Ns);

        var result = await ReconcileAsync(preview);

        preview.Status!.Phase.ShouldBe(PreviewPhase.Ready);
        Condition(preview, PreviewConditions.Ready).Status.ShouldBe("True");
        result.RequeueAfter.ShouldBe(_options.ResyncPeriod);
        _cluster.Events.Select(e => e.Reason).ShouldBe([PreviewPhase.Provisioning, PreviewPhase.Ready]);
    }

    [Fact]
    public async Task ReadyCloseToExpiry_IsRequeuedAtExpiry()
    {
        var preview = await DeployAsync(Previews.ForPullRequest(42, ttl: "10m"));
        _cluster.MarkApiAvailable(Ns);
        _clock.SetUtcNow(Previews.Created.AddMinutes(8));

        var result = await ReconcileAsync(preview);

        result.RequeueAfter.ShouldBe(TimeSpan.FromMinutes(2));
    }

    [Fact]
    public async Task FailedDeploymentJob_FailsThePreviewWithAWarning()
    {
        var preview = await DeployAsync(Previews.ForPullRequest(42), succeeded: false);

        preview.Status!.Phase.ShouldBe(PreviewPhase.Failed);
        Condition(preview, PreviewConditions.ApplicationDeployed).Reason.ShouldBe("DeploymentFailed");
        _cluster.Events.Last().Type.ShouldBe("Warning");
    }

    [Fact]
    public async Task NewImage_RunsANewDeploymentJob()
    {
        var preview = await DeployAsync(Previews.ForPullRequest(42));
        var firstJob = _cluster.Jobs(Ns).Single().Metadata.Name;

        preview.Spec.Image = "ghcr.io/example/sample-api:9e8d7c6";
        preview.Metadata.Generation = 2;
        await ReconcileAsync(preview);

        _cluster.Jobs(Ns).Select(j => j.Metadata.Name).ShouldContain(firstJob);
        _cluster.Jobs(Ns).Count().ShouldBe(2);
        preview.Status!.ObservedGeneration.ShouldBe(2);
    }

    [Fact]
    public async Task SeedData_IsLoadedAfterTheDeploymentBeforeReady()
    {
        var preview = await DeployAsync(Previews.ForPullRequest(42, seed: true));
        _cluster.MarkApiAvailable(Ns);

        await ReconcileAsync(preview);
        var seedJob = _cluster.Jobs(Ns).Single(j => j.Metadata.Name.StartsWith("seed-", StringComparison.Ordinal));
        preview.Status!.Phase.ShouldBe(PreviewPhase.Provisioning);

        _cluster.CompleteJob(Ns, seedJob.Metadata.Name);
        await ReconcileAsync(preview);

        preview.Status.Phase.ShouldBe(PreviewPhase.Ready);
    }

    [Theory]
    [InlineData("100h", "exceeds the maximum")]
    [InlineData("soon", "is not a duration")]
    public async Task InvalidTtl_FailsWithoutCreatingAnything(string ttl, string message)
    {
        var preview = Previews.ForPullRequest(42, ttl: ttl);

        await ReconcileAsync(preview);

        preview.Status!.Phase.ShouldBe(PreviewPhase.Failed);
        preview.Status.Message.ShouldNotBeNull().ShouldContain(message);
        _cluster.ApplyLog.ShouldBeEmpty();
    }

    [Fact]
    public async Task UntaggedImage_IsRejected()
    {
        var preview = Previews.ForPullRequest(42);
        preview.Spec.Image = "ghcr.io/example/sample-api";

        await ReconcileAsync(preview);

        Condition(preview, PreviewConditions.Ready).Reason.ShouldBe("InvalidSpec");
    }

    [Fact]
    public async Task ExpiredPreview_IsMarkedExpiredAndDeleted()
    {
        var preview = Previews.ForPullRequest(42, ttl: "1h");
        _clock.SetUtcNow(Previews.Created.AddHours(1));

        var result = await ReconcileAsync(preview);

        preview.Status!.Phase.ShouldBe(PreviewPhase.Expired);
        _cluster.DeletedPreviews.ShouldBe([preview.Metadata.Name]);
        result.RequeueAfter.ShouldBeNull();
    }

    [Fact]
    public async Task DeletedPreview_DeletesTheNamespaceThenReleasesTheFinalizer()
    {
        var preview = Previews.ForPullRequest(42);
        await ReconcileAsync(preview);
        preview.Metadata.DeletionTimestamp = _clock.GetUtcNow().UtcDateTime;

        var whileDeleting = await ReconcileAsync(preview);

        _cluster.DeletedNamespaces.ShouldBe([Ns]);
        whileDeleting.RequeueAfter.ShouldBe(PreviewReconciler.DeletionPollInterval);
        preview.Metadata.Finalizers.ShouldContain(PreviewReconciler.Finalizer);

        _cluster.RemoveNamespace(Ns);
        var done = await ReconcileAsync(preview);

        preview.Metadata.Finalizers.ShouldNotContain(PreviewReconciler.Finalizer);
        done.RequeueAfter.ShouldBeNull();
        _cluster.Events.Last().Reason.ShouldBe("Deleted");
    }

    [Fact]
    public async Task DeletedSecret_IsRebuiltFromTheStoredCredentials()
    {
        var preview = Previews.ForPullRequest(42);
        await ReconcileAsync(preview);
        var appPassword = Secret(PreviewManifests.DatabaseCredentialsSecret, "APP_PASSWORD");

        var fresh = new FakeClusterGateway();
        fresh.Add(preview);
        await fresh.CreateSecretIfMissingAsync(
            _cluster.Get<k8s.Models.V1Secret>("Secret", Ns, PreviewManifests.DatabaseCredentialsSecret)!,
            TestContext.Current.CancellationToken);
        var reconciler = new PreviewReconciler(fresh, new SequentialSecretGenerator(), Options.Create(_options), _clock,
            NullLogger<PreviewReconciler>.Instance);

        await reconciler.ReconcileAsync(preview, TestContext.Current.CancellationToken);

        var connection = fresh.SecretValue(Ns, PreviewManifests.ApiSecret, "Database__ConnectionString");
        connection.ShouldContain($"Password={appPassword}");
    }

    private async Task<ReconcileResult> ReconcileAsync(PreviewEnvironment preview) =>
        await _reconciler.ReconcileAsync(preview, TestContext.Current.CancellationToken);

    private async Task<PreviewEnvironment> DeployAsync(PreviewEnvironment preview, bool succeeded = true)
    {
        await ReconcileAsync(preview);
        _cluster.MarkDatabaseReady(Ns);
        await ReconcileAsync(preview);
        _cluster.CompleteJob(Ns, _cluster.Jobs(Ns).Single().Metadata.Name, succeeded);
        await ReconcileAsync(preview);
        return preview;
    }

    private string Secret(string name, string key) => _cluster.SecretValue(Ns, name, key);

    private static k8s.Models.V1Condition Condition(PreviewEnvironment preview, string type) =>
        preview.Status!.Conditions.Single(c => c.Type == type);
}
