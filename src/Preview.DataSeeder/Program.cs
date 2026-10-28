using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Preview.DataSeeder;
using Sample.Application;
using Sample.Infrastructure;
using Sample.Infrastructure.Persistence;

// Usage: Preview.DataSeeder [synthetic|pseudonymize]
//   synthetic (default)  generates a fictitious dataset (fixed seed) into Database:ConnectionString
//   pseudonymize         copies Source:ConnectionString into Database:ConnectionString, pseudonymised with
//                        Pseudonymization:Salt (base64, at least 32 bytes, kept in a secret)
var mode = args.FirstOrDefault() ?? "synthetic";
if (mode is not ("synthetic" or "pseudonymize"))
{
    await Console.Error.WriteLineAsync($"Unknown mode '{mode}'. Expected 'synthetic' or 'pseudonymize'.");
    return 2;
}

var builder = Host.CreateApplicationBuilder(args.Skip(1).ToArray());
builder.Configuration.AddKeyPerFile(builder.Configuration["Secrets:Path"] ?? "/var/run/secrets/app", optional: true);
builder.Logging.ClearProviders();
builder.Logging.AddJsonConsole(options => options.UseUtcTimestamp = true);
builder.Services.AddApplication(builder.Configuration).AddInfrastructure(builder.Configuration);

using var host = builder.Build();
var logger = host.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Preview.DataSeeder");
using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(5));

try
{
    await using var scope = host.Services.CreateAsyncScope();
    var target = new DatasetStore(scope.ServiceProvider.GetRequiredService<AppDbContext>());
    if (await target.IsSeededAsync(timeout.Token))
    {
        logger.AlreadySeeded();
        return 0;
    }

    var dataset = mode == "synthetic"
        ? SyntheticDataset.Generate(TimeProvider.System.GetUtcNow())
        : await ReadPseudonymizedAsync(builder.Configuration, timeout.Token);

    await target.WriteAsync(dataset, timeout.Token);
    logger.Seeded(mode, dataset.Employees.Count, dataset.AbsenceRequests.Count);
    return 0;
}
catch (Exception ex)
{
    logger.SeedFailed(ex);
    return 1;
}

static async Task<Dataset> ReadPseudonymizedAsync(IConfiguration configuration, CancellationToken cancellationToken)
{
    var salt = Convert.FromBase64String(configuration["Pseudonymization:Salt"]
        ?? throw new InvalidOperationException("Pseudonymization:Salt is required in pseudonymize mode."));
    var sourceConnection = configuration["Source:ConnectionString"]
        ?? throw new InvalidOperationException("Source:ConnectionString is required in pseudonymize mode.");

    await using var source = Sample.Infrastructure.DependencyInjection.CreateDbContext(sourceConnection);
    var original = await new DatasetStore(source).ReadAsync(cancellationToken);
    return new Pseudonymizer(salt).Pseudonymize(original);
}
