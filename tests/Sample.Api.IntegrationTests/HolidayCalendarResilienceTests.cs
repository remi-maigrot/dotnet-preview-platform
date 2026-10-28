using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Sample.Application;
using Sample.Application.Abstractions;
using Sample.Domain.Absences;
using Sample.Infrastructure;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;

namespace Sample.Api.IntegrationTests;

/// <summary>Exercises the real HTTP resilience pipeline against a fake calendar server.</summary>
public sealed class HolidayCalendarResilienceTests : IDisposable
{
    private const string Path = "/PublicHolidays/2026/FR";
    private static readonly DateRange Year2026 = DateRange.Create(new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31)).Value;

    private readonly WireMockServer _server = WireMockServer.Start();

    [Fact]
    public async Task TransientFailures_AreRetried()
    {
        _server.Given(Request.Create().WithPath(Path)).InScenario("flaky").WillSetStateTo("failed-once")
            .RespondWith(Response.Create().WithStatusCode(503));
        _server.Given(Request.Create().WithPath(Path)).InScenario("flaky").WhenStateIs("failed-once").WillSetStateTo("failed-twice")
            .RespondWith(Response.Create().WithStatusCode(500));
        _server.Given(Request.Create().WithPath(Path)).InScenario("flaky").WhenStateIs("failed-twice")
            .RespondWith(Response.Create().WithStatusCode(200).WithBodyAsJson(new[] { new { date = "2026-06-15", name = "Company day" } }));

        // Generous attempt timeout: this test is about retries, a slow first call must not turn into a timeout.
        var holidays = await CreateProvider(attemptTimeout: "00:00:02").GetHolidaysAsync(Year2026, TestContext.Current.CancellationToken);

        _server.LogEntries.Select(e => Convert.ToInt32(e.ResponseMessage!.StatusCode, System.Globalization.CultureInfo.InvariantCulture)).ShouldBe([503, 500, 200]);
        holidays.ShouldBe([new DateOnly(2026, 6, 15)]);
    }

    [Fact]
    public async Task PersistentFailure_FallsBackToTheBuiltInCalendar()
    {
        _server.Given(Request.Create().WithPath(Path)).RespondWith(Response.Create().WithStatusCode(500));

        var holidays = await CreateProvider().GetHolidaysAsync(Year2026, TestContext.Current.CancellationToken);

        _server.LogEntries.Count.ShouldBe(3, "one call and two retries");
        holidays.ShouldBe(FrenchPublicHolidays.For(2026), ignoreOrder: true);
    }

    [Fact]
    public async Task SlowServer_IsCutByTheAttemptTimeout()
    {
        _server.Given(Request.Create().WithPath(Path))
            .RespondWith(Response.Create().WithDelay(TimeSpan.FromSeconds(5)).WithBodyAsJson(Array.Empty<object>()));

        var started = TimeProvider.System.GetTimestamp();
        var holidays = await CreateProvider().GetHolidaysAsync(Year2026, TestContext.Current.CancellationToken);

        // The server answers after 5 s: finishing well before proves the attempts were cut short.
        TimeProvider.System.GetElapsedTime(started).ShouldBeLessThan(TimeSpan.FromSeconds(4));
        holidays.ShouldBe(FrenchPublicHolidays.For(2026), ignoreOrder: true);
    }

    public void Dispose() => _server.Dispose();

    private IPublicHolidayProvider CreateProvider(string attemptTimeout = "00:00:00.300")
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Database:ConnectionString"] = "Host=unused",
            ["Features:ExternalHolidayCalendar"] = "true",
            ["HolidayCalendar:BaseAddress"] = _server.Url,
            ["HolidayCalendar:AttemptTimeout"] = attemptTimeout,
            ["HolidayCalendar:TotalTimeout"] = "00:00:10",
            ["HolidayCalendar:MaxRetryAttempts"] = "2",
        }).Build();

        var services = new ServiceCollection()
            .AddLogging()
            .AddApplication(configuration)
            .AddInfrastructure(configuration)
            .BuildServiceProvider();
        return services.CreateScope().ServiceProvider.GetRequiredService<IPublicHolidayProvider>();
    }
}
