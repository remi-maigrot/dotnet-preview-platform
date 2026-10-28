using Devctl.Cli.Kubernetes;
using Preview.Operator.Crds;

namespace Devctl.Cli.Tests.TestSupport;

/// <summary>A cluster where applied previews become Ready immediately, unless told otherwise.</summary>
internal sealed class FakePlatformClient : IPlatformClient
{
    public Dictionary<string, PreviewSummary> Previews { get; } = [];

    public List<PreviewEnvironment> Applied { get; } = [];

    public HashSet<string> UnhealthyDeployments { get; } = [];

    private long _generation;

    public string PhaseAfterApply { get; set; } = PreviewPhase.Ready;

    /// <summary>Number of reads that still return the status of the previous generation, as a lagging operator would.</summary>
    public int StaleStatusReads { get; set; }

    public Task<IReadOnlyList<PreviewSummary>> ListPreviewsAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<PreviewSummary>>([.. Previews.Values.OrderBy(p => p.PullRequest)]);

    public int Reads { get; private set; }

    public Task<PreviewSummary?> GetPreviewAsync(string name, CancellationToken cancellationToken)
    {
        Reads++;
        var preview = Previews.GetValueOrDefault(name);
        if (preview is not null && StaleStatusReads > 0 && --StaleStatusReads == 0)
        {
            Previews[name] = preview = preview with { ObservedGeneration = preview.Generation };
        }

        return Task.FromResult(preview);
    }

    public Task ApplyPreviewAsync(PreviewEnvironment preview, CancellationToken cancellationToken)
    {
        Applied.Add(preview);
        var url = $"http://{preview.Metadata.Name}.preview.localtest.me:8080";
        Previews[preview.Metadata.Name] = new PreviewSummary(
            preview.Spec.PullRequest, preview.Metadata.Name, preview.Spec.Image, PhaseAfterApply, url,
            DateTimeOffset.UnixEpoch.AddDays(1), DateTimeOffset.UnixEpoch, PhaseAfterApply == PreviewPhase.Failed ? "deployment failed" : null)
        {
            Generation = ++_generation,
            ObservedGeneration = StaleStatusReads > 0 ? _generation - 1 : _generation,
        };
        return Task.CompletedTask;
    }

    public Task<bool> DeletePreviewAsync(string name, CancellationToken cancellationToken) => Task.FromResult(Previews.Remove(name));

    public Task<ComponentHealth> GetDeploymentHealthAsync(string @namespace, string name, CancellationToken cancellationToken) =>
        Task.FromResult(UnhealthyDeployments.Contains(name)
            ? new ComponentHealth(name, @namespace, false, "0/1 ready")
            : new ComponentHealth(name, @namespace, true, "1/1 ready"));
}
