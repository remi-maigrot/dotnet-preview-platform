using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Sample.Application.Absences;
using Sample.Application.Configuration;
using Sample.Application.Diagnostics;

namespace Sample.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<FeatureFlagsOptions>().Bind(configuration.GetSection(FeatureFlagsOptions.SectionName));
        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<AbsenceMetrics>();
        services.AddScoped<SubmitAbsenceRequestHandler>();
        services.AddScoped<ChangeAbsenceRequestHandler>();
        services.AddScoped<AbsenceRequestQueries>();
        return services;
    }
}
