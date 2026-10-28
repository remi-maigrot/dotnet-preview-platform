using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using k8s;
using k8s.Models;
using Preview.Operator.Crds;

namespace Preview.Operator.Reconciliation;

/// <summary>
/// Desired state of one preview environment, as plain Kubernetes objects. Pure functions: same input, same output,
/// which is what makes the reconciliation idempotent and testable without a cluster.
/// </summary>
internal sealed class PreviewManifests
{
    public const string FieldManager = "preview-operator";
    public const string PullRequestLabel = "platform.remimaigrot.dev/pull-request";
    public const string PartOfLabel = "app.kubernetes.io/part-of";
    public const string PartOf = "preview-platform";
    public const string ReleaseName = "sample-api";
    public const string ApiDeploymentName = "sample-api";
    public const string DatabaseName = "postgresql";
    public const string DatabaseCredentialsSecret = "postgres-credentials";
    public const string ApiSecret = "sample-api-secrets";
    public const string MigratorSecret = "sample-api-migrator-secrets";
    public const string DeployerServiceAccount = "deployer";
    public const string ValuesConfigMap = "preview-values";

    private const string Database = "absences";
    private const string AppRole = "app";
    private const int PostgresUid = 70;
    private const int NonRootUid = 65532;

    private readonly PreviewEnvironment _preview;
    private readonly OperatorOptions _options;
    private readonly ImageReference _apiImage;

    public PreviewManifests(PreviewEnvironment preview, OperatorOptions options, ImageReference apiImage)
    {
        _preview = preview;
        _options = options;
        _apiImage = apiImage;
        Namespace = NamespaceFor(preview.Spec.PullRequest);
        Host = $"pr-{preview.Spec.PullRequest}.{options.PreviewDomain}";
    }

    public string Namespace { get; }

    public string Host { get; }

    public string Url => _options.PublicPort is { } port
        ? $"{_options.UrlScheme}://{Host}:{port.ToString(CultureInfo.InvariantCulture)}"
        : $"{_options.UrlScheme}://{Host}";

    /// <summary>Changes whenever what is deployed changes; names the deployment job so each change runs once.</summary>
    public string DeploymentHash => Hash(_options.DeployerImage + "\n" + HelmValues());

    public string DeployJobName => $"deploy-{DeploymentHash}";

    public string SeedJobName => $"seed-{Hash(_apiImage + "\n" + _options.SeederImage)}";

    public static string NamespaceFor(int pullRequest) => $"pr-{pullRequest.ToString(CultureInfo.InvariantCulture)}";

    /// <summary>Objects applied on every reconciliation, in dependency order.</summary>
    public IReadOnlyList<IKubernetesObject<V1ObjectMeta>> Resources() =>
    [
        PreviewNamespace(),
        OperatorRoleBinding(),
        Quota(),
        Limits(),
        DenyAll(),
        AllowDns(),
        AllowDatabaseClients(),
        AllowDatabaseEgress(),
        AllowDeployerToApiServer(),
        DatabaseInitScript(),
        DatabaseService(),
        DatabaseStatefulSet(),
        DeployerServiceAccountObject(),
        DeployerRoleBinding(),
        Values(),
    ];

    public V1Namespace PreviewNamespace() => new()
    {
        ApiVersion = "v1",
        Kind = "Namespace",
        Metadata = new V1ObjectMeta
        {
            Name = Namespace,
            Labels = Labels(new Dictionary<string, string>
            {
                // Pod Security admission rejects anything that is not "restricted": no root, no privilege escalation.
                ["pod-security.kubernetes.io/enforce"] = "restricted",
                ["pod-security.kubernetes.io/enforce-version"] = "latest",
                ["pod-security.kubernetes.io/warn"] = "restricted",
            }),
            // Namespace and PreviewEnvironment are both cluster-scoped, so the garbage collector deletes the
            // namespace (and everything inside) when the PreviewEnvironment goes away, even if the operator is down.
            OwnerReferences =
            [
                new V1OwnerReference
                {
                    ApiVersion = PreviewEnvironment.ApiVersionName,
                    Kind = PreviewEnvironment.KindName,
                    Name = _preview.Metadata.Name,
                    Uid = _preview.Metadata.Uid,
                    Controller = true,
                    BlockOwnerDeletion = true,
                },
            ],
        },
    };

    public V1ResourceQuota Quota() => new()
    {
        ApiVersion = "v1",
        Kind = "ResourceQuota",
        Metadata = Meta("preview-quota"),
        Spec = new V1ResourceQuotaSpec
        {
            Hard = new Dictionary<string, ResourceQuantity>
            {
                ["requests.cpu"] = new(_options.QuotaCpu),
                ["requests.memory"] = new(_options.QuotaMemory),
                ["limits.cpu"] = new(_options.QuotaCpu),
                ["limits.memory"] = new(_options.QuotaMemory),
                ["pods"] = new("20"),
                ["services.loadbalancers"] = new("0"),
                ["services.nodeports"] = new("0"),
                ["persistentvolumeclaims"] = new("0"),
            },
        },
    };

    public V1LimitRange Limits() => new()
    {
        ApiVersion = "v1",
        Kind = "LimitRange",
        Metadata = Meta("preview-limits"),
        Spec = new V1LimitRangeSpec
        {
            Limits =
            [
                new V1LimitRangeItem
                {
                    Type = "Container",
                    DefaultRequest = new Dictionary<string, ResourceQuantity> { ["cpu"] = new("50m"), ["memory"] = new("64Mi") },
                    DefaultProperty = new Dictionary<string, ResourceQuantity> { ["cpu"] = new("500m"), ["memory"] = new("256Mi") },
                    Max = new Dictionary<string, ResourceQuantity> { ["cpu"] = new("1"), ["memory"] = new("1Gi") },
                },
            ],
        },
    };

    public V1NetworkPolicy DenyAll() => Policy("default-deny", new V1NetworkPolicySpec
    {
        PodSelector = new V1LabelSelector(),
        PolicyTypes = ["Ingress", "Egress"],
    });

    public V1NetworkPolicy AllowDns() => Policy("allow-dns", new V1NetworkPolicySpec
    {
        PodSelector = new V1LabelSelector(),
        PolicyTypes = ["Egress"],
        Egress =
        [
            new V1NetworkPolicyEgressRule
            {
                To =
                [
                    new V1NetworkPolicyPeer
                    {
                        NamespaceSelector = Selector("kubernetes.io/metadata.name", "kube-system"),
                        PodSelector = Selector("k8s-app", "kube-dns"),
                    },
                ],
                Ports = [Port(53, "UDP"), Port(53, "TCP")],
            },
        ],
    });

    /// <summary>The database accepts connections from the platform's own pods (API, migrations, seeder) only.</summary>
    public V1NetworkPolicy AllowDatabaseClients() => Policy("allow-database-clients", new V1NetworkPolicySpec
    {
        PodSelector = Selector("app.kubernetes.io/name", DatabaseName),
        PolicyTypes = ["Ingress"],
        Ingress =
        [
            new V1NetworkPolicyIngressRule
            {
                FromProperty = [new V1NetworkPolicyPeer { PodSelector = Selector(PartOfLabel, PartOf) }],
                Ports = [Port(5432, "TCP")],
            },
        ],
    });

    /// <summary>Counterpart of <see cref="AllowDatabaseClients"/>: platform pods may open connections to the database.</summary>
    public V1NetworkPolicy AllowDatabaseEgress() => Policy("allow-egress-to-database", new V1NetworkPolicySpec
    {
        PodSelector = Selector(PartOfLabel, PartOf),
        PolicyTypes = ["Egress"],
        Egress =
        [
            new V1NetworkPolicyEgressRule
            {
                To = [new V1NetworkPolicyPeer { PodSelector = Selector("app.kubernetes.io/name", DatabaseName) }],
                Ports = [Port(5432, "TCP")],
            },
        ],
    });

    /// <summary>helm talks to the API server; its address is not known in advance, the ports are.</summary>
    public V1NetworkPolicy AllowDeployerToApiServer() => Policy("allow-deployer-to-apiserver", new V1NetworkPolicySpec
    {
        PodSelector = Selector("app.kubernetes.io/component", "deployer"),
        PolicyTypes = ["Egress"],
        Egress = [new V1NetworkPolicyEgressRule { Ports = [Port(443, "TCP"), Port(6443, "TCP")] }],
    });

    /// <summary>Creates the application role with data-only privileges: the API cannot alter the schema.</summary>
    public V1ConfigMap DatabaseInitScript() => new()
    {
        ApiVersion = "v1",
        Kind = "ConfigMap",
        Metadata = Meta("postgres-init"),
        Data = new Dictionary<string, string>
        {
            ["10-app-role.sh"] = $"""
                #!/bin/sh
                set -eu
                psql -v ON_ERROR_STOP=1 -v app_password="$APP_PASSWORD" --username "$POSTGRES_USER" --dbname "$POSTGRES_DB" <<'SQL'
                CREATE ROLE {AppRole} LOGIN PASSWORD :'app_password';
                GRANT CONNECT ON DATABASE {Database} TO {AppRole};
                GRANT USAGE ON SCHEMA public TO {AppRole};
                ALTER DEFAULT PRIVILEGES IN SCHEMA public GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO {AppRole};
                ALTER DEFAULT PRIVILEGES IN SCHEMA public GRANT USAGE, SELECT ON SEQUENCES TO {AppRole};
                SQL
                """,
        },
    };

    public V1Service DatabaseService() => new()
    {
        ApiVersion = "v1",
        Kind = "Service",
        Metadata = Meta(DatabaseName, DatabaseLabels()),
        Spec = new V1ServiceSpec
        {
            Selector = new Dictionary<string, string> { ["app.kubernetes.io/name"] = DatabaseName },
            Ports = [new V1ServicePort { Name = "postgres", Port = 5432, TargetPort = 5432 }],
        },
    };

    /// <summary>Ephemeral PostgreSQL: data lives in an emptyDir and disappears with the preview, by design.</summary>
    public V1StatefulSet DatabaseStatefulSet() => new()
    {
        ApiVersion = "apps/v1",
        Kind = "StatefulSet",
        Metadata = Meta(DatabaseName, DatabaseLabels()),
        Spec = new V1StatefulSetSpec
        {
            ServiceName = DatabaseName,
            Replicas = 1,
            Selector = new V1LabelSelector { MatchLabels = new Dictionary<string, string> { ["app.kubernetes.io/name"] = DatabaseName } },
            Template = new V1PodTemplateSpec
            {
                Metadata = new V1ObjectMeta { Labels = DatabaseLabels() },
                Spec = new V1PodSpec
                {
                    AutomountServiceAccountToken = false,
                    SecurityContext = PodSecurity(PostgresUid),
                    Containers =
                    [
                        new V1Container
                        {
                            Name = "postgres",
                            Image = _options.PostgresImage,
                            ImagePullPolicy = _options.ImagePullPolicy,
                            Ports = [new V1ContainerPort { Name = "postgres", ContainerPort = 5432 }],
                            Env =
                            [
                                new V1EnvVar { Name = "POSTGRES_DB", Value = Database },
                                new V1EnvVar { Name = "PGDATA", Value = "/var/lib/postgresql/data/pgdata" },
                                SecretEnv("POSTGRES_PASSWORD", DatabaseCredentialsSecret, "POSTGRES_PASSWORD"),
                                SecretEnv("APP_PASSWORD", DatabaseCredentialsSecret, "APP_PASSWORD"),
                            ],
                            SecurityContext = ContainerSecurity(),
                            Resources = Resources("100m", "256Mi", "500m", "512Mi"),
                            ReadinessProbe = new V1Probe
                            {
                                Exec = new V1ExecAction { Command = ["pg_isready", "-U", "postgres", "-d", Database] },
                                PeriodSeconds = 3,
                                FailureThreshold = 3,
                            },
                            VolumeMounts =
                            [
                                new V1VolumeMount { Name = "data", MountPath = "/var/lib/postgresql/data" },
                                new V1VolumeMount { Name = "run", MountPath = "/var/run/postgresql" },
                                new V1VolumeMount { Name = "tmp", MountPath = "/tmp" },
                                new V1VolumeMount { Name = "init", MountPath = "/docker-entrypoint-initdb.d", ReadOnlyProperty = true },
                            ],
                        },
                    ],
                    Volumes =
                    [
                        new V1Volume { Name = "data", EmptyDir = new V1EmptyDirVolumeSource { SizeLimit = new ResourceQuantity("1Gi") } },
                        new V1Volume { Name = "run", EmptyDir = new V1EmptyDirVolumeSource() },
                        new V1Volume { Name = "tmp", EmptyDir = new V1EmptyDirVolumeSource() },
                        new V1Volume { Name = "init", ConfigMap = new V1ConfigMapVolumeSource { Name = "postgres-init", DefaultMode = 365 } },
                    ],
                },
            },
        },
    };

    /// <summary>
    /// Gives the operator its working permissions in this namespace only. Outside preview namespaces it can
    /// create namespaces and bindings, but cannot read or write anything else.
    /// </summary>
    public V1RoleBinding OperatorRoleBinding() => new()
    {
        ApiVersion = "rbac.authorization.k8s.io/v1",
        Kind = "RoleBinding",
        Metadata = Meta(_options.ServiceAccount),
        RoleRef = new V1RoleRef { ApiGroup = "rbac.authorization.k8s.io", Kind = "ClusterRole", Name = _options.NamespaceClusterRole },
        Subjects = [new Rbacv1Subject { Kind = "ServiceAccount", Name = _options.ServiceAccount, NamespaceProperty = _options.Namespace }],
    };

    public V1ServiceAccount DeployerServiceAccountObject() => new()
    {
        ApiVersion = "v1",
        Kind = "ServiceAccount",
        Metadata = Meta(DeployerServiceAccount),
    };

    /// <summary>
    /// Grants the deployer the chart's permissions in this namespace only. The operator does not hold these
    /// permissions itself: it has the <c>bind</c> verb on that single ClusterRole.
    /// </summary>
    public V1RoleBinding DeployerRoleBinding() => new()
    {
        ApiVersion = "rbac.authorization.k8s.io/v1",
        Kind = "RoleBinding",
        Metadata = Meta(DeployerServiceAccount),
        RoleRef = new V1RoleRef { ApiGroup = "rbac.authorization.k8s.io", Kind = "ClusterRole", Name = _options.DeployerClusterRole },
        Subjects = [new Rbacv1Subject { Kind = "ServiceAccount", Name = DeployerServiceAccount, NamespaceProperty = Namespace }],
    };

    public V1ConfigMap Values() => new()
    {
        ApiVersion = "v1",
        Kind = "ConfigMap",
        Metadata = Meta(ValuesConfigMap),
        Data = new Dictionary<string, string> { ["values.yaml"] = HelmValues() },
    };

    /// <summary>Credentials of the preview database, generated once and then left untouched.</summary>
    public IReadOnlyList<V1Secret> Secrets(string postgresPassword, string appPassword, string signingKey)
    {
        var host = $"{DatabaseName}.{Namespace}.svc";
        return
        [
            Secret(DatabaseCredentialsSecret, new()
            {
                ["POSTGRES_PASSWORD"] = postgresPassword,
                ["APP_PASSWORD"] = appPassword,
            }),
            Secret(ApiSecret, new()
            {
                ["Database__ConnectionString"] = $"Host={host};Database={Database};Username={AppRole};Password={appPassword}",
                ["Authentication__Jwt__SigningKey"] = signingKey,
            }),
            Secret(MigratorSecret, new()
            {
                ["Database__ConnectionString"] = $"Host={host};Database={Database};Username=postgres;Password={postgresPassword}",
            }),
        ];
    }

    /// <summary>Runs <c>helm upgrade --install</c> with the chart baked into the deployer image.</summary>
    public V1Job DeployJob() => Job(
        DeployJobName,
        component: "deployer",
        image: _options.DeployerImage,
        serviceAccount: DeployerServiceAccount,
        args:
        [
            "upgrade", "--install", ReleaseName, "/charts/sample-api",
            "--namespace", Namespace,
            "--values", "/config/values.yaml",
            "--wait", "--timeout", "5m",
            "--rollback-on-failure",
        ],
        configMap: ValuesConfigMap);

    public V1Job SeedJob() => Job(
        SeedJobName,
        component: "seeder",
        image: _options.SeederImage ?? throw new InvalidOperationException("Operator:SeederImage is not configured."),
        serviceAccount: null,
        args: ["synthetic"],
        configMap: null,
        secret: MigratorSecret);

    internal string HelmValues()
    {
        var resources = _preview.Spec.Resources ?? new PreviewResources();
        var migrator = _apiImage.Sibling("sample-migrator");
        var builder = new StringBuilder()
            .AppendLine("# Generated by preview-operator for " + _preview.Metadata.Name + ". Do not edit.")
            .AppendLine("image:")
            .AppendLine($"  repository: {_apiImage.Repository}")
            .AppendLine($"  tag: \"{_apiImage.Tag}\"")
            .AppendLine($"  pullPolicy: {_options.ImagePullPolicy}")
            .AppendLine("migrations:")
            .AppendLine("  image:")
            .AppendLine($"    repository: {migrator.Repository}")
            .AppendLine($"  existingSecret: {MigratorSecret}")
            .AppendLine("secrets:")
            .AppendLine($"  existingSecret: {ApiSecret}")
            .AppendLine("environment: Preview")
            .AppendLine("replicaCount: 1")
            .AppendLine("autoscaling:")
            .AppendLine("  enabled: false")
            .AppendLine("podDisruptionBudget:")
            .AppendLine("  enabled: false")
            .AppendLine("resources:")
            .AppendLine("  requests:")
            .AppendLine("    cpu: 50m")
            .AppendLine("    memory: 128Mi")
            .AppendLine("  limits:")
            .AppendLine($"    cpu: \"{resources.Cpu}\"")
            .AppendLine($"    memory: \"{resources.Memory}\"")
            .AppendLine("ingress:")
            .AppendLine("  enabled: true")
            .AppendLine($"  host: {Host}")
            .AppendLine("networkPolicy:")
            .AppendLine($"  ingressControllerNamespace: {_options.IngressControllerNamespace}")
            .AppendLine("  collector:")
            .AppendLine($"    namespace: {_options.CollectorNamespace}")
            .AppendLine("opentelemetry:")
            .AppendLine($"  endpoint: \"{_options.CollectorEndpoint}\"")
            .AppendLine($"  resourceAttributes: \"preview.pull_request={_preview.Spec.PullRequest.ToString(CultureInfo.InvariantCulture)}\"")
            .AppendLine("config:")
            .AppendLine("  Features__AutoApproveSickLeave: \"true\"")
            .AppendLine("release:")
            .AppendLine($"  sha: \"{_preview.Spec.CommitSha ?? _apiImage.Tag}\"");
        return builder.ToString();
    }

    private V1Job Job(
        string name,
        string component,
        string image,
        string? serviceAccount,
        IList<string> args,
        string? configMap,
        string? secret = null)
    {
        var labels = Labels(new Dictionary<string, string> { ["app.kubernetes.io/component"] = component });
        var mounts = new List<V1VolumeMount> { new() { Name = "tmp", MountPath = "/tmp" } };
        var volumes = new List<V1Volume> { new() { Name = "tmp", EmptyDir = new V1EmptyDirVolumeSource() } };
        if (configMap is not null)
        {
            mounts.Add(new V1VolumeMount { Name = "config", MountPath = "/config", ReadOnlyProperty = true });
            volumes.Add(new V1Volume { Name = "config", ConfigMap = new V1ConfigMapVolumeSource { Name = configMap } });
        }

        if (secret is not null)
        {
            mounts.Add(new V1VolumeMount { Name = "secrets", MountPath = "/var/run/secrets/app", ReadOnlyProperty = true });
            volumes.Add(new V1Volume { Name = "secrets", Secret = new V1SecretVolumeSource { SecretName = secret } });
        }

        return new V1Job
        {
            ApiVersion = "batch/v1",
            Kind = "Job",
            Metadata = Meta(name, labels),
            Spec = new V1JobSpec
            {
                BackoffLimit = 1,
                ActiveDeadlineSeconds = 600,
                TtlSecondsAfterFinished = 3600,
                Template = new V1PodTemplateSpec
                {
                    Metadata = new V1ObjectMeta { Labels = labels },
                    Spec = new V1PodSpec
                    {
                        RestartPolicy = "Never",
                        ServiceAccountName = serviceAccount,
                        AutomountServiceAccountToken = serviceAccount is not null,
                        SecurityContext = PodSecurity(NonRootUid),
                        Containers =
                        [
                            new V1Container
                            {
                                Name = component,
                                Image = image,
                                ImagePullPolicy = _options.ImagePullPolicy,
                                Args = args,
                                Env =
                                [
                                    new V1EnvVar { Name = "HOME", Value = "/tmp" },
                                    new V1EnvVar { Name = "Secrets__Path", Value = "/var/run/secrets/app" },
                                ],
                                SecurityContext = ContainerSecurity(),
                                Resources = Resources("50m", "64Mi", "500m", "256Mi"),
                                VolumeMounts = mounts,
                            },
                        ],
                        Volumes = volumes,
                    },
                },
            },
        };
    }

    private static string Hash(string value) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)))[..10];

    private V1ObjectMeta Meta(string name, IDictionary<string, string>? labels = null) => new()
    {
        Name = name,
        NamespaceProperty = Namespace,
        Labels = labels ?? Labels(),
    };

    private Dictionary<string, string> Labels(IDictionary<string, string>? extra = null)
    {
        var labels = new Dictionary<string, string>
        {
            ["app.kubernetes.io/managed-by"] = FieldManager,
            [PartOfLabel] = PartOf,
            [PullRequestLabel] = _preview.Spec.PullRequest.ToString(CultureInfo.InvariantCulture),
        };
        foreach (var (key, value) in extra ?? new Dictionary<string, string>())
        {
            labels[key] = value;
        }

        return labels;
    }

    private Dictionary<string, string> DatabaseLabels() =>
        Labels(new Dictionary<string, string> { ["app.kubernetes.io/name"] = DatabaseName, ["app.kubernetes.io/component"] = "database" });

    private V1NetworkPolicy Policy(string name, V1NetworkPolicySpec spec) => new()
    {
        ApiVersion = "networking.k8s.io/v1",
        Kind = "NetworkPolicy",
        Metadata = Meta(name),
        Spec = spec,
    };

    private V1Secret Secret(string name, Dictionary<string, string> data) => new()
    {
        ApiVersion = "v1",
        Kind = "Secret",
        Metadata = Meta(name),
        Type = "Opaque",
        StringData = data,
    };

    private static V1LabelSelector Selector(string key, string value) =>
        new() { MatchLabels = new Dictionary<string, string> { [key] = value } };

    private static V1NetworkPolicyPort Port(int port, string protocol) => new() { Port = port, Protocol = protocol };

    private static V1EnvVar SecretEnv(string name, string secret, string key) => new()
    {
        Name = name,
        ValueFrom = new V1EnvVarSource { SecretKeyRef = new V1SecretKeySelector { Name = secret, Key = key } },
    };

    private static V1PodSecurityContext PodSecurity(int uid) => new()
    {
        RunAsNonRoot = true,
        RunAsUser = uid,
        RunAsGroup = uid,
        FsGroup = uid,
        SeccompProfile = new V1SeccompProfile { Type = "RuntimeDefault" },
    };

    private static V1SecurityContext ContainerSecurity() => new()
    {
        AllowPrivilegeEscalation = false,
        ReadOnlyRootFilesystem = true,
        Capabilities = new V1Capabilities { Drop = ["ALL"] },
    };

    private static V1ResourceRequirements Resources(string cpuRequest, string memoryRequest, string cpuLimit, string memoryLimit) => new()
    {
        Requests = new Dictionary<string, ResourceQuantity> { ["cpu"] = new(cpuRequest), ["memory"] = new(memoryRequest) },
        Limits = new Dictionary<string, ResourceQuantity> { ["cpu"] = new(cpuLimit), ["memory"] = new(memoryLimit) },
    };
}
