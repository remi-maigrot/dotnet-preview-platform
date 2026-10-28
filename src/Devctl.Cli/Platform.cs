namespace Devctl.Cli;

/// <summary>Names shared by the local cluster, the manifests and the commands.</summary>
internal static class Platform
{
    public const string ClusterName = "preview-platform";
    public const string KubeContext = "k3d-" + ClusterName;

    /// <summary>The registry created with the cluster: pushed to from the host, pulled from inside the cluster.</summary>
    public const string RegistryName = "preview-registry.localhost";
    public const int RegistryPort = 5050;
    public const string RegistryFromHost = "localhost:5050";
    public const string RegistryFromCluster = RegistryName + ":5050";

    public const int DefaultHttpPort = 8080;
    public const string PreviewDomain = "preview.localtest.me";
    public const string OperatorNamespace = "preview-system";
    public const string ObservabilityNamespace = "observability";
    public const string GrafanaHost = "grafana." + PreviewDomain;

    public static readonly string[] Images = ["sample-api", "sample-migrator", "preview-operator", "sample-deployer", "sample-seeder"];

    public static string NamespaceOf(int pullRequest) => pullRequest == 0 ? "local" : $"pr-{pullRequest}";

    public static string PreviewName(int pullRequest) => NamespaceOf(pullRequest);
}
