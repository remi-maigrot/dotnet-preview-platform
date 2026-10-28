using k8s.Models;
using Microsoft.Extensions.Options;
using Preview.Operator.Crds;
using Preview.Operator.Diagnostics;

namespace Preview.Operator.Reconciliation;

/// <summary>
/// Brings one PreviewEnvironment to its desired state. Every call starts from what is in the cluster and applies
/// the full desired state again, so running it twice, or after a crash halfway through, gives the same result.
/// </summary>
internal sealed class PreviewReconciler(
    IClusterGateway cluster,
    ISecretGenerator secrets,
    IOptions<OperatorOptions> options,
    TimeProvider clock,
    ILogger<PreviewReconciler> logger)
{
    public const string Finalizer = "platform.remimaigrot.dev/cleanup";

    internal static readonly TimeSpan ProvisioningPollInterval = TimeSpan.FromSeconds(5);
    internal static readonly TimeSpan DeletionPollInterval = TimeSpan.FromSeconds(3);

    public async Task<ReconcileResult> ReconcileAsync(PreviewEnvironment preview, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(preview);

        if (preview.Metadata.DeletionTimestamp is not null)
        {
            return await FinalizeAsync(preview, cancellationToken);
        }

        if (!(preview.Metadata.Finalizers?.Contains(Finalizer) ?? false))
        {
            await cluster.SetFinalizerAsync(preview, present: true, cancellationToken);
        }

        var now = clock.GetUtcNow();
        var status = CloneStatus(preview.Status);
        status.ObservedGeneration = preview.Metadata.Generation;
        status.Namespace = PreviewManifests.NamespaceFor(preview.Spec.PullRequest);

        var validation = Validate(preview, out var ttl, out var image);
        if (validation is not null)
        {
            return await FailAsync(preview, status, "InvalidSpec", validation, cancellationToken);
        }

        var created = preview.Metadata.CreationTimestamp is { } timestamp
            ? new DateTimeOffset(DateTime.SpecifyKind(timestamp, DateTimeKind.Utc))
            : now;
        var expiresAt = created + ttl;
        status.ExpiresAt = expiresAt;
        if (now >= expiresAt)
        {
            return await ExpireAsync(preview, status, cancellationToken);
        }

        var manifests = new PreviewManifests(preview, options.Value, image);
        status.Url = manifests.Url;

        foreach (var resource in manifests.Resources())
        {
            await cluster.ApplyAsync(resource, cancellationToken);
        }

        Conditions.Set(status.Conditions, PreviewConditions.NamespaceReady, Conditions.True, "Applied",
            $"Namespace {manifests.Namespace} and its policies are in place.", preview.Metadata.Generation, now);

        await EnsureSecretsAsync(manifests, cancellationToken);

        var databaseReady = await IsDatabaseReadyAsync(manifests, cancellationToken);
        Conditions.Set(status.Conditions, PreviewConditions.DatabaseReady,
            databaseReady ? Conditions.True : Conditions.False,
            databaseReady ? "Running" : "Starting",
            databaseReady ? "PostgreSQL accepts connections." : "Waiting for PostgreSQL to become ready.",
            preview.Metadata.Generation, now);

        var deployment = databaseReady
            ? await DeployAsync(manifests, cancellationToken)
            : JobOutcome.NotStarted;
        if (deployment == JobOutcome.Failed)
        {
            Conditions.Set(status.Conditions, PreviewConditions.ApplicationDeployed, Conditions.False, "DeploymentFailed",
                $"Job {manifests.DeployJobName} failed. Inspect it with: kubectl -n {manifests.Namespace} logs job/{manifests.DeployJobName}",
                preview.Metadata.Generation, now);
            return await FailAsync(preview, status, "DeploymentFailed",
                $"Deployment of {preview.Spec.Image} failed; see the ApplicationDeployed condition.", cancellationToken);
        }

        var seeded = deployment == JobOutcome.Succeeded && preview.Spec.SeedData
            ? await SeedAsync(manifests, cancellationToken)
            : JobOutcome.Succeeded;
        if (seeded == JobOutcome.Failed)
        {
            return await FailAsync(preview, status, "SeedFailed",
                $"Job {manifests.SeedJobName} could not load the sample data.", cancellationToken);
        }

        var (deployedStatus, deployedReason, deployedMessage) = deployment switch
        {
            JobOutcome.Succeeded => (Conditions.True, "Deployed", $"{preview.Spec.Image} is deployed."),
            JobOutcome.NotStarted => (Conditions.False, "WaitingForDatabase", "The deployment starts once PostgreSQL is ready."),
            _ => (Conditions.False, "InProgress", $"Deploying {preview.Spec.Image} (job {manifests.DeployJobName})."),
        };
        Conditions.Set(status.Conditions, PreviewConditions.ApplicationDeployed, deployedStatus, deployedReason,
            deployedMessage, preview.Metadata.Generation, now);

        var serving = deployment == JobOutcome.Succeeded && seeded == JobOutcome.Succeeded
            && await IsApiAvailableAsync(manifests, cancellationToken);
        Conditions.Set(status.Conditions, PreviewConditions.Ready,
            serving ? Conditions.True : Conditions.False,
            serving ? "Serving" : "Provisioning",
            serving ? $"Available at {manifests.Url}" : "The environment is being provisioned.",
            preview.Metadata.Generation, now);

        status.Message = null;
        await TransitionAsync(preview, status, serving ? PreviewPhase.Ready : PreviewPhase.Provisioning, cancellationToken);

        // Once ready, the only scheduled event is the expiry; drift is caught by the periodic resync.
        return serving
            ? ReconcileResult.After(Min(expiresAt - now, options.Value.ResyncPeriod))
            : ReconcileResult.After(Min(expiresAt - now, ProvisioningPollInterval));
    }

    private string? Validate(PreviewEnvironment preview, out TimeSpan ttl, out ImageReference image)
    {
        image = new ImageReference(string.Empty, null, null);
        if (!Ttl.TryParse(preview.Spec.Ttl, out ttl))
        {
            return $"spec.ttl '{preview.Spec.Ttl}' is not a duration such as 2h or 90m.";
        }

        if (ttl > options.Value.MaxTtl)
        {
            return $"spec.ttl {preview.Spec.Ttl} exceeds the maximum of {options.Value.MaxTtl.TotalHours:0}h allowed by the platform.";
        }

        if (!ImageReference.TryParse(preview.Spec.Image, out image) || image.Tag is null)
        {
            return $"spec.image '{preview.Spec.Image}' must be a tagged image reference (registry/name:commit-sha).";
        }

        if (preview.Spec.SeedData && string.IsNullOrEmpty(options.Value.SeederImage))
        {
            return "spec.seedData is true but the operator has no seeder image configured.";
        }

        return null;
    }

    private async Task EnsureSecretsAsync(PreviewManifests manifests, CancellationToken cancellationToken)
    {
        // Passwords are generated once. Later reconciliations rebuild the derived secrets from the stored ones,
        // so a derived secret deleted or changed by hand is restored without breaking the database it points to.
        var stored = await cluster.GetSecretAsync(manifests.Namespace, PreviewManifests.DatabaseCredentialsSecret, cancellationToken);
        var postgresPassword = Read(stored, "POSTGRES_PASSWORD") ?? secrets.Generate(24);
        var appPassword = Read(stored, "APP_PASSWORD") ?? secrets.Generate(24);

        var api = await cluster.GetSecretAsync(manifests.Namespace, PreviewManifests.ApiSecret, cancellationToken);
        var signingKey = Read(api, "Authentication__Jwt__SigningKey") ?? secrets.Generate(48);

        var all = manifests.Secrets(postgresPassword, appPassword, signingKey);
        await cluster.CreateSecretIfMissingAsync(all[0], cancellationToken);
        foreach (var derived in all.Skip(1))
        {
            await cluster.ApplyAsync(derived, cancellationToken);
        }
    }

    private async Task<bool> IsDatabaseReadyAsync(PreviewManifests manifests, CancellationToken cancellationToken)
    {
        var database = await cluster.GetStatefulSetAsync(manifests.Namespace, PreviewManifests.DatabaseName, cancellationToken);
        return database?.Status?.ReadyReplicas >= 1;
    }

    private async Task<bool> IsApiAvailableAsync(PreviewManifests manifests, CancellationToken cancellationToken)
    {
        var api = await cluster.GetDeploymentAsync(manifests.Namespace, PreviewManifests.ApiDeploymentName, cancellationToken);
        return api?.Status?.AvailableReplicas >= 1;
    }

    private async Task<JobOutcome> DeployAsync(PreviewManifests manifests, CancellationToken cancellationToken)
    {
        var job = await cluster.GetJobAsync(manifests.Namespace, manifests.DeployJobName, cancellationToken);
        if (job is null)
        {
            await cluster.ApplyAsync(manifests.DeployJob(), cancellationToken);
            logger.DeploymentStarted(manifests.Namespace, manifests.DeployJobName);
            return JobOutcome.Running;
        }

        return Outcome(job);
    }

    private async Task<JobOutcome> SeedAsync(PreviewManifests manifests, CancellationToken cancellationToken)
    {
        var job = await cluster.GetJobAsync(manifests.Namespace, manifests.SeedJobName, cancellationToken);
        if (job is null)
        {
            await cluster.ApplyAsync(manifests.SeedJob(), cancellationToken);
            return JobOutcome.Running;
        }

        return Outcome(job);
    }

    private async Task<ReconcileResult> FailAsync(
        PreviewEnvironment preview,
        PreviewEnvironmentStatus status,
        string reason,
        string message,
        CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        status.Message = message;
        Conditions.Set(status.Conditions, PreviewConditions.Ready, Conditions.False, reason, message, preview.Metadata.Generation, now);
        await TransitionAsync(preview, status, PreviewPhase.Failed, cancellationToken);

        // A failure needs a new spec (new commit, fixed TTL) to recover; only the expiry still has to be honoured.
        return status.ExpiresAt is { } expiresAt
            ? ReconcileResult.After(Min(expiresAt - now, options.Value.ResyncPeriod))
            : ReconcileResult.After(options.Value.ResyncPeriod);
    }

    private async Task<ReconcileResult> ExpireAsync(
        PreviewEnvironment preview,
        PreviewEnvironmentStatus status,
        CancellationToken cancellationToken)
    {
        status.Message = "The time to live is over; the environment is being deleted.";
        await TransitionAsync(preview, status, PreviewPhase.Expired, cancellationToken);
        await cluster.DeletePreviewAsync(preview.Metadata.Name, cancellationToken);
        logger.PreviewExpired(preview.Metadata.Name);
        return ReconcileResult.Done;
    }

    private async Task<ReconcileResult> FinalizeAsync(PreviewEnvironment preview, CancellationToken cancellationToken)
    {
        var name = PreviewManifests.NamespaceFor(preview.Spec.PullRequest);
        if (await cluster.NamespaceExistsAsync(name, cancellationToken))
        {
            await cluster.DeleteNamespaceAsync(name, cancellationToken);
            return ReconcileResult.After(DeletionPollInterval);
        }

        if (preview.Metadata.Finalizers?.Contains(Finalizer) ?? false)
        {
            await cluster.RecordEventAsync(preview, "Normal", "Deleted", $"Namespace {name} and all its resources are gone.", cancellationToken);
            await cluster.SetFinalizerAsync(preview, present: false, cancellationToken);
        }

        return ReconcileResult.Done;
    }

    private async Task TransitionAsync(
        PreviewEnvironment preview,
        PreviewEnvironmentStatus status,
        string phase,
        CancellationToken cancellationToken)
    {
        var previous = preview.Status?.Phase;
        status.Phase = phase;
        await cluster.UpdateStatusAsync(preview, status, cancellationToken);

        if (previous != phase)
        {
            var type = phase == PreviewPhase.Failed ? "Warning" : "Normal";
            await cluster.RecordEventAsync(preview, type, phase, status.Message ?? $"Preview is {phase}.", cancellationToken);
            logger.PhaseChanged(preview.Metadata.Name, previous ?? "none", phase);
        }
    }

    private static JobOutcome Outcome(V1Job job)
    {
        var conditions = job.Status?.Conditions ?? [];
        if (conditions.Any(c => c.Type == "Complete" && c.Status == Conditions.True))
        {
            return JobOutcome.Succeeded;
        }

        return conditions.Any(c => c.Type == "Failed" && c.Status == Conditions.True) ? JobOutcome.Failed : JobOutcome.Running;
    }

    private static PreviewEnvironmentStatus CloneStatus(PreviewEnvironmentStatus? status) => new()
    {
        Phase = status?.Phase ?? PreviewPhase.Pending,
        Url = status?.Url,
        Namespace = status?.Namespace,
        ExpiresAt = status?.ExpiresAt,
        ObservedGeneration = status?.ObservedGeneration,
        Message = status?.Message,
        Conditions = [.. status?.Conditions ?? []],
    };

    private static string? Read(V1Secret? secret, string key) =>
        secret?.Data is { } data && data.TryGetValue(key, out var bytes) ? System.Text.Encoding.UTF8.GetString(bytes) : null;

    private static TimeSpan Min(TimeSpan a, TimeSpan b) => a < b ? (a < TimeSpan.Zero ? TimeSpan.Zero : a) : b;

    private enum JobOutcome
    {
        NotStarted,
        Running,
        Succeeded,
        Failed,
    }
}
