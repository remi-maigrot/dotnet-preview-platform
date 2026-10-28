using k8s;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using Preview.Operator;
using Preview.Operator.Controller;
using Preview.Operator.Diagnostics;
using Preview.Operator.Kubernetes;
using Preview.Operator.Reconciliation;

var builder = WebApplication.CreateSlimBuilder(args);

builder.Logging.ClearProviders();
builder.Logging.AddJsonConsole(options => options.UseUtcTimestamp = true);

builder.Services.AddOptions<OperatorOptions>()
    .Bind(builder.Configuration.GetSection(OperatorOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<IKubernetes>(_ => new Kubernetes(
    KubernetesClientConfiguration.IsInCluster()
        ? KubernetesClientConfiguration.InClusterConfig()
        : KubernetesClientConfiguration.BuildConfigFromConfigFile()));
builder.Services.AddSingleton<IClusterGateway, KubernetesClusterGateway>();
builder.Services.AddSingleton<ISecretGenerator, RandomSecretGenerator>();
builder.Services.AddScoped<PreviewReconciler>();
builder.Services.AddSingleton<OperatorMetrics>();
builder.Services.AddSingleton<PreviewController>();
builder.Services.AddHostedService<LeaderElectedService>();

builder.Services.AddOpenTelemetry()
    .ConfigureResource(resource => resource.AddService("preview-operator"))
    .WithMetrics(metrics => metrics
        .AddMeter(OperatorMetrics.MeterName)
        .AddPrometheusExporter());

builder.Services.AddHealthChecks();

var app = builder.Build();
app.MapHealthChecks("/healthz");
app.MapPrometheusScrapingEndpoint("/metrics");
await app.RunAsync();
