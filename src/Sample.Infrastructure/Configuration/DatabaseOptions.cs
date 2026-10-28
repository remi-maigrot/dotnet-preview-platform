using System.ComponentModel.DataAnnotations;

namespace Sample.Infrastructure.Configuration;

/// <summary>Bound from the <c>Database</c> section. The connection string comes from a mounted secret, never from appsettings.</summary>
public sealed class DatabaseOptions
{
    public const string SectionName = "Database";

    [Required(ErrorMessage = "Database:ConnectionString is required. Provide it through the Database__ConnectionString environment variable or the mounted secret file.")]
    public string ConnectionString { get; set; } = string.Empty;

    [Range(1, 300)]
    public int CommandTimeoutSeconds { get; set; } = 30;

    [Range(0, 10)]
    public int MaxRetryCount { get; set; } = 3;
}
