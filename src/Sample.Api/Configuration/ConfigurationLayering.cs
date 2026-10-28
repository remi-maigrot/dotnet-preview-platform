namespace Sample.Api.Configuration;

internal static class ConfigurationLayering
{
    public const string DefaultSecretsPath = "/var/run/secrets/sample-api";

    /// <summary>
    /// Final layer on top of the defaults (appsettings.json → appsettings.{Environment}.json → user secrets in
    /// Development → environment variables → command line): one file per key, mounted from a Kubernetes Secret.
    /// A file named <c>Database__ConnectionString</c> sets <c>Database:ConnectionString</c>.
    /// </summary>
    public static WebApplicationBuilder AddLayeredConfiguration(this WebApplicationBuilder builder)
    {
        var secretsPath = builder.Configuration["Secrets:Path"] ?? DefaultSecretsPath;
        builder.Configuration.AddKeyPerFile(secretsPath, optional: true, reloadOnChange: false);
        return builder;
    }
}
