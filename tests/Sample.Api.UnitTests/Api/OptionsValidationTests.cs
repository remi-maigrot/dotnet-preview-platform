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

    [Theory]
    [InlineData(180, 1825, true)]
    [InlineData(1825, 1825, false)]
    [InlineData(2000, 1825, false)]
    public void DataRetention_FreeTextMustGoBeforeTheRequest(int freeTextDays, int requestDays, bool valid)
    {
        var options = new DataRetentionOptions
        {
            FreeTextRetention = TimeSpan.FromDays(freeTextDays),
            RequestRetention = TimeSpan.FromDays(requestDays),
        };
        var results = new List<System.ComponentModel.DataAnnotations.ValidationResult>();

        System.ComponentModel.DataAnnotations.Validator.TryValidateObject(options, new(options), results, validateAllProperties: true)
            .ShouldBe(valid);
    }

    [Theory]
    [InlineData("Production", true, false)]
    [InlineData("Production", false, true)]
    [InlineData("Preview", true, true)]
    [InlineData("Development", true, true)]
    public void FaultInjection_IsRefusedInProduction(string environment, bool enabled, bool valid)
    {
        var host = Substitute.For<Microsoft.Extensions.Hosting.IHostEnvironment>();
        host.EnvironmentName.Returns(environment);

        var result = new FaultInjectionOptionsValidator(host).Validate(null, new FaultInjectionOptions { Enabled = enabled, ErrorRatio = 0.5 });

        result.Succeeded.ShouldBe(valid);
    }

    private static IOptionsMonitor<FeatureFlagsOptions> Flags(bool external)
    {
        var monitor = Substitute.For<IOptionsMonitor<FeatureFlagsOptions>>();
        monitor.CurrentValue.Returns(new FeatureFlagsOptions { ExternalHolidayCalendar = external });
        return monitor;
    }
}
