using Microsoft.Extensions.Options;
using NSubstitute;
using Sample.Api.Configuration;
using Sample.Application.Configuration;
using Sample.Infrastructure.Configuration;

namespace Sample.Api.UnitTests.Api;

public sealed class OptionsValidationTests
{
    private const string Key = "a-signing-key-that-is-long-enough-123";

    [Theory]
    [InlineData(null, null, false)]
    [InlineData("https://idp.example.test", Key, false)]
    [InlineData("https://idp.example.test", null, true)]
    [InlineData(null, Key, true)]
    public void Jwt_RequiresExactlyOneValidationMode(string? authority, string? signingKey, bool valid)
    {
        var options = new JwtOptions { Issuer = "issuer", Audience = "audience", Authority = authority, SigningKey = signingKey };

        new JwtOptionsValidator().Validate(null, options).Succeeded.ShouldBe(valid);
    }

    [Fact]
    public void HolidayCalendar_ExternalFlagWithoutAddress_Fails()
    {
        var validator = new HolidayCalendarOptionsValidator(Flags(external: true));

        var result = validator.Validate(null, new HolidayCalendarOptions());

        result.Failed.ShouldBeTrue();
        result.FailureMessage.ShouldContain("HolidayCalendar:BaseAddress");
    }

    [Fact]
    public void HolidayCalendar_TotalTimeoutNotAboveAttemptTimeout_Fails()
    {
        var validator = new HolidayCalendarOptionsValidator(Flags(external: false));
        var options = new HolidayCalendarOptions { AttemptTimeout = TimeSpan.FromSeconds(5), TotalTimeout = TimeSpan.FromSeconds(5) };

        validator.Validate(null, options).Failed.ShouldBeTrue();
    }

    [Fact]
    public void HolidayCalendar_Defaults_AreValid() =>
        new HolidayCalendarOptionsValidator(Flags(external: false))
            .Validate(null, new HolidayCalendarOptions()).Succeeded.ShouldBeTrue();

    private static IOptionsMonitor<FeatureFlagsOptions> Flags(bool external)
    {
        var monitor = Substitute.For<IOptionsMonitor<FeatureFlagsOptions>>();
        monitor.CurrentValue.Returns(new FeatureFlagsOptions { ExternalHolidayCalendar = external });
        return monitor;
    }
}
