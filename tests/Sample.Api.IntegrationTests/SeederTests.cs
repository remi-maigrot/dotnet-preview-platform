extern alias seeder;

using System.Net.Http.Json;
using Sample.Api.Contracts;
using Sample.Api.IntegrationTests.Infrastructure;
using seeder::Preview.DataSeeder;

namespace Sample.Api.IntegrationTests;

/// <summary>The seeder writes into the migrated schema, and what it writes is usable through the API.</summary>
public sealed class SeederTests(PostgresFixture postgres)
{
    [Fact]
    public async Task SyntheticData_IsWrittenOnceAndServedByTheApi()
    {
        await using (var db = postgres.CreateDbContext())
        {
            var store = new DatasetStore(db);
            if (!await store.IsSeededAsync(TestContext.Current.CancellationToken))
            {
                await store.WriteAsync(SyntheticDataset.Generate(DateTimeOffset.UtcNow), TestContext.Current.CancellationToken);
            }

            (await store.IsSeededAsync(TestContext.Current.CancellationToken)).ShouldBeTrue();
            var readBack = await store.ReadAsync(TestContext.Current.CancellationToken);
            readBack.Employees.ShouldContain(e => e.Id == KnownAccounts.Employee);
        }

        await using var factory = new ApiFactory(postgres);
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", TestTokens.For(KnownAccounts.Manager, manager: true));

        var page = await client.GetFromJsonAsync<PagedResponse<AbsenceRequestResponse>>("/v1/absence-requests", TestContext.Current.CancellationToken);

        // The known manager has a single report (the smoke employee), who starts with no history.
        page!.TotalCount.ShouldBe(0);
        var employee = new HttpClient(factory.Server.CreateHandler()) { BaseAddress = factory.Server.BaseAddress };
        employee.DefaultRequestHeaders.Authorization = new("Bearer", TestTokens.For(KnownAccounts.Employee));
        var monday = Dates.Monday(30);
        var response = await employee.SendAsync(new HttpRequestMessage(HttpMethod.Post, "/v1/absence-requests")
        {
            Content = JsonContent.Create(new SubmitAbsenceRequest(AbsenceType.PaidLeave, monday, monday.AddDays(2), null)),
            Headers = { { ApiHeaders.IdempotencyKey, Guid.NewGuid().ToString() } },
        }, TestContext.Current.CancellationToken);
        response.StatusCode.ShouldBe(System.Net.HttpStatusCode.Created);
    }
}
