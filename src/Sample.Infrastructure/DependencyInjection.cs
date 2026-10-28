using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Options;
using Polly;
using Sample.Application.Abstractions;
using Sample.Infrastructure.Configuration;
using Sample.Infrastructure.Holidays;
using Sample.Infrastructure.Idempotency;
using Sample.Infrastructure.Persistence;
using Sample.Infrastructure.Persistence.Repositories;
using Sample.Infrastructure.Retention;

namespace Sample.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddOptions<DatabaseOptions>()
            .Bind(configuration.GetSection(DatabaseOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();
        services.AddOptions<HolidayCalendarOptions>()
            .Bind(configuration.GetSection(HolidayCalendarOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<HolidayCalendarOptions>, HolidayCalendarOptionsValidator>();
        services.AddOptions<IdempotencyOptions>()
            .Bind(configuration.GetSection(IdempotencyOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();
        services.AddOptions<DataRetentionOptions>()
            .Bind(configuration.GetSection(DataRetentionOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddDbContext<AppDbContext>((sp, options) =>
        {
            var database = sp.GetRequiredService<IOptions<DatabaseOptions>>().Value;
            ConfigureDbContext(options, database.ConnectionString, database);
        });
        services.AddScoped<IUnitOfWork, EfUnitOfWork>();
        services.AddScoped<IEmployeeRepository, EmployeeRepository>();
        services.AddScoped<IAbsenceRequestRepository, AbsenceRequestRepository>();
        services.AddScoped<IIdempotencyStore, EfIdempotencyStore>();
        services.AddHostedService<IdempotencyPurgeService>();
        services.AddScoped<DataRetentionPurger>();
        services.AddHostedService<DataRetentionService>();

        services.AddSingleton<BuiltInHolidayProvider>();
        services.AddScoped<IPublicHolidayProvider, PublicHolidayProvider>();
        services.AddHttpClient<HolidayCalendarClient>((sp, http) =>
            {
                var options = sp.GetRequiredService<IOptions<HolidayCalendarOptions>>().Value;
                if (options.BaseAddress is not null)
                {
                    http.BaseAddress = new Uri(options.BaseAddress.TrimEnd('/') + "/");
                }
            })
            .AddResilienceHandler("holiday-calendar", (pipeline, context) =>
            {
                var options = context.ServiceProvider.GetRequiredService<IOptions<HolidayCalendarOptions>>().Value;
                // Outer to inner: total timeout, retry, circuit breaker, per-attempt timeout.
                pipeline.AddTimeout(options.TotalTimeout);
                if (options.MaxRetryAttempts > 0)
                {
                    pipeline.AddRetry(new HttpRetryStrategyOptions
                    {
                        MaxRetryAttempts = options.MaxRetryAttempts,
                        Delay = TimeSpan.FromMilliseconds(200),
                        UseJitter = true,
                    });
                }

                pipeline
                    .AddCircuitBreaker(new HttpCircuitBreakerStrategyOptions
                    {
                        FailureRatio = 0.5,
                        MinimumThroughput = 5,
                        SamplingDuration = TimeSpan.FromSeconds(30),
                        BreakDuration = TimeSpan.FromSeconds(15),
                    })
                    .AddTimeout(options.AttemptTimeout);
            });

        return services;
    }

    /// <summary>A context outside dependency injection, for tools that talk to several databases (seeder, release checks).</summary>
    public static AppDbContext CreateDbContext(string connectionString)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>();
        ConfigureDbContext(options, connectionString);
        return new AppDbContext(options.Options);
    }

    internal static void ConfigureDbContext(DbContextOptionsBuilder options, string connectionString, DatabaseOptions? database = null)
    {
        database ??= new DatabaseOptions();
        options
            .UseNpgsql(connectionString, npgsql =>
            {
                npgsql.CommandTimeout(database.CommandTimeoutSeconds);
                npgsql.MigrationsHistoryTable("__ef_migrations_history");
                if (database.MaxRetryCount > 0)
                {
                    npgsql.EnableRetryOnFailure(database.MaxRetryCount);
                }
            })
            .UseSnakeCaseNamingConvention();
    }
}
