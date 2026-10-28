using Microsoft.Extensions.Options;
using Sample.Application.Configuration;

namespace Sample.Infrastructure.Configuration;

/// <summary>Cross-section rule that data annotations cannot express: the external calendar needs an address.</summary>
internal sealed class HolidayCalendarOptionsValidator(IOptionsMonitor<FeatureFlagsOptions> features)
    : IValidateOptions<HolidayCalendarOptions>
{
    public ValidateOptionsResult Validate(string? name, HolidayCalendarOptions options)
    {
        if (features.CurrentValue.ExternalHolidayCalendar && string.IsNullOrWhiteSpace(options.BaseAddress))
        {
            return ValidateOptionsResult.Fail(
                "HolidayCalendar:BaseAddress is required when Features:ExternalHolidayCalendar is enabled.");
        }

        if (options.TotalTimeout <= options.AttemptTimeout)
        {
            return ValidateOptionsResult.Fail("HolidayCalendar:TotalTimeout must be greater than HolidayCalendar:AttemptTimeout.");
        }

        return ValidateOptionsResult.Success;
    }
}
