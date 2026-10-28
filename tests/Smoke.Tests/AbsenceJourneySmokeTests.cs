using System.Net;
using System.Net.Http.Json;
using Sample.Api.Contracts;

namespace Smoke.Tests;

/// <summary>The main user journey, end to end, on the seeded accounts. Leaves no data behind (the request is cancelled).</summary>
public sealed class AbsenceJourneySmokeTests
{
    [Fact]
    public async Task Employee_Submits_Manager_Approves_Employee_Cancels()
    {
        using var employee = SmokeEnvironment.AsEmployee();
        using var manager = SmokeEnvironment.AsManager();
        var start = NextMonday().AddDays(7 * Random.Shared.Next(20, 200));
        var body = new SubmitAbsenceRequest(AbsenceType.PaidLeave, start, start.AddDays(2), "smoke test");
        var key = Guid.NewGuid().ToString();

        var created = await SubmitAsync(employee, body, key);
        created.StatusCode.ShouldBe(HttpStatusCode.Created);
        var request = (await created.Content.ReadFromJsonAsync<AbsenceRequestResponse>(TestContext.Current.CancellationToken))!;

        var replay = await SubmitAsync(employee, body, key);
        replay.StatusCode.ShouldBe(HttpStatusCode.Created);
        replay.Headers.GetValues(ApiHeaders.IdempotentReplayed).ShouldHaveSingleItem().ShouldBe("true");

        var approved = await manager.PostAsync($"/v1/absence-requests/{request.Id}/approve", null, TestContext.Current.CancellationToken);
        approved.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await approved.Content.ReadFromJsonAsync<AbsenceRequestResponse>(TestContext.Current.CancellationToken))!
            .Status.ShouldBe(AbsenceStatus.Approved);

        var cancelled = await employee.PostAsync($"/v1/absence-requests/{request.Id}/cancel", null, TestContext.Current.CancellationToken);
        cancelled.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task InvalidInput_IsAProblemDetails()
    {
        using var employee = SmokeEnvironment.AsEmployee();
        var start = NextMonday();

        var response = await SubmitAsync(employee, new SubmitAbsenceRequest(AbsenceType.PaidLeave, start, start.AddDays(-3), null), Guid.NewGuid().ToString());

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        response.Content.Headers.ContentType!.MediaType.ShouldBe("application/problem+json");
    }

    private static Task<HttpResponseMessage> SubmitAsync(HttpClient client, SubmitAbsenceRequest body, string key)
    {
        var message = new HttpRequestMessage(HttpMethod.Post, "/v1/absence-requests") { Content = JsonContent.Create(body) };
        message.Headers.Add(ApiHeaders.IdempotencyKey, key);
        return client.SendAsync(message, TestContext.Current.CancellationToken);
    }

    private static DateOnly NextMonday()
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        return today.AddDays(((int)DayOfWeek.Monday - (int)today.DayOfWeek + 7) % 7 + 7);
    }
}
