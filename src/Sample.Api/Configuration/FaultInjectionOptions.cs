using System.ComponentModel.DataAnnotations;
using Microsoft.Extensions.Options;

namespace Sample.Api.Configuration;

/// <summary>
/// Makes the API fail or slow down on purpose, to rehearse a canary rollback or an alert. Refused at startup in
/// Production: there, a fault is an incident, not a rehearsal.
/// </summary>
internal sealed class FaultInjectionOptions
{
    public const string SectionName = "FaultInjection";

    public bool Enabled { get; set; }

    /// <summary>Share of API calls answered 500, between 0 and 1.</summary>
    [Range(0d, 1d)]
    public double ErrorRatio { get; set; }

    /// <summary>Added to every API call before it is handled.</summary>
    [Range(typeof(TimeSpan), "00:00:00", "00:00:10")]
    public TimeSpan Delay { get; set; }
}

internal sealed class FaultInjectionOptionsValidator(IHostEnvironment environment) : IValidateOptions<FaultInjectionOptions>
{
    public ValidateOptionsResult Validate(string? name, FaultInjectionOptions options) =>
        options.Enabled && environment.IsProduction()
            ? ValidateOptionsResult.Fail("FaultInjection:Enabled cannot be true in the Production environment.")
            : ValidateOptionsResult.Success;
}
