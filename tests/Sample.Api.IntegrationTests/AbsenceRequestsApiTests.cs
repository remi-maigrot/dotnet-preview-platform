using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Sample.Api.Contracts;
using Sample.Api.IntegrationTests.Infrastructure;

namespace Sample.Api.IntegrationTests;

public sealed class AbsenceRequestsApiTests(PostgresFixture postgres) : IAsyncLifetime
{
    private readonly ApiFactory _factory = new(postgres);
    private Team _team = null!;

    public async ValueTask InitializeAsync() => _team = await Team.CreateAsync(postgres);

    [Fact]
    public async Task Submit_ValidRequest_Returns201WithLocation()
    {
        var client = Team.As(_factory.CreateClient(), _team.Alice);

        var response = await SubmitAsync(client, Request(weeksAhead: 2));

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        var body = await response.Content.ReadFromJsonAsync<AbsenceRequestResponse>(TestContext.Current.CancellationToken);
        body.ShouldNotBeNull();
        body.Status.ShouldBe(AbsenceStatus.Submitted);
        body.WorkingDays.ShouldBeInRange(3, 5);
        response.Headers.Location!.ToString().ShouldEndWith($"/v1/absence-requests/{body.Id}");

        var fetched = await client.GetFromJsonAsync<AbsenceRequestResponse>(response.Headers.Location, TestContext.Current.CancellationToken);
        fetched.ShouldBe(body);
    }

    [Fact]
    public async Task Submit_SameKeyAndBody_ReplaysTheOriginalResponse()
    {
        var client = Team.As(_factory.CreateClient(), _team.Alice);
        var key = Guid.NewGuid().ToString();
        var request = Request(weeksAhead: 3);

        var first = await SubmitAsync(client, request, key);
        var second = await SubmitAsync(client, request, key);

        second.StatusCode.ShouldBe(HttpStatusCode.Created);
        second.Headers.GetValues(ApiHeaders.IdempotentReplayed).ShouldHaveSingleItem().ShouldBe("true");
        second.Headers.Location.ShouldBe(first.Headers.Location);
        var firstBody = await first.Content.ReadFromJsonAsync<AbsenceRequestResponse>(TestContext.Current.CancellationToken);
        var secondBody = await second.Content.ReadFromJsonAsync<AbsenceRequestResponse>(TestContext.Current.CancellationToken);
        secondBody.ShouldBe(firstBody);

        var list = await client.GetFromJsonAsync<PagedResponse<AbsenceRequestResponse>>("/v1/absence-requests", TestContext.Current.CancellationToken);
        list!.TotalCount.ShouldBe(1);
    }

    [Fact]
    public async Task Submit_SameKeyDifferentBody_Returns422()
    {
        var client = Team.As(_factory.CreateClient(), _team.Alice);
        var key = Guid.NewGuid().ToString();
        await SubmitAsync(client, Request(weeksAhead: 4), key);

        var response = await SubmitAsync(client, Request(weeksAhead: 5), key);

        await ShouldBeProblemAsync(response, HttpStatusCode.UnprocessableEntity, ErrorCodes.IdempotencyKeyReused);
    }

    [Fact]
    public async Task Submit_WithoutIdempotencyKey_Returns400()
    {
        var client = Team.As(_factory.CreateClient(), _team.Alice);

        var response = await SubmitAsync(client, Request(weeksAhead: 2), key: null);

        await ShouldBeProblemAsync(response, HttpStatusCode.BadRequest, ErrorCodes.IdempotencyKeyMissing);
    }

    [Fact]
    public async Task Submit_MissingFields_Returns400ValidationProblem()
    {
        var client = Team.As(_factory.CreateClient(), _team.Alice);

        var response = await SubmitAsync(client, new SubmitAbsenceRequest(null, null, null, null));

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var problem = await response.Content.ReadFromJsonAsync<HttpValidationProblemDetails>(TestContext.Current.CancellationToken);
        problem!.Errors.Keys.ShouldBe(["Type", "StartDate", "EndDate"], ignoreOrder: true);
    }

    [Fact]
    public async Task Submit_OverlappingPeriod_Returns409WithBusinessCode()
    {
        var client = Team.As(_factory.CreateClient(), _team.Alice);
        await SubmitAsync(client, Request(weeksAhead: 6));

        var response = await SubmitAsync(client, Request(weeksAhead: 6));

        await ShouldBeProblemAsync(response, HttpStatusCode.Conflict, "absence.overlapping");
    }

    [Fact]
    public async Task Submit_WithoutToken_Returns401()
    {
        var response = await SubmitAsync(_factory.CreateClient(), Request(weeksAhead: 2));

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Submit_TokenSignedWithAnotherKey_Returns401()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer",
            TestTokens.For(_team.Alice.Id, signingKey: "a-completely-different-signing-key-0123456789"));

        var response = await SubmitAsync(client, Request(weeksAhead: 2));

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Approve_ByTheManager_Returns200()
    {
        var created = await SubmitAndReadAsync(Team.As(_factory.CreateClient(), _team.Alice), weeksAhead: 7);
        var manager = Team.As(_factory.CreateClient(), _team.Manager, manager: true);

        var response = await manager.PostAsync($"/v1/absence-requests/{created.Id}/approve", null, TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<AbsenceRequestResponse>(TestContext.Current.CancellationToken);
        body!.Status.ShouldBe(AbsenceStatus.Approved);
        body.DecidedBy.ShouldBe(_team.Manager.Id);
    }

    [Fact]
    public async Task Approve_WithoutManagerRole_Returns403()
    {
        var created = await SubmitAndReadAsync(Team.As(_factory.CreateClient(), _team.Alice), weeksAhead: 8);
        var bob = Team.As(_factory.CreateClient(), _team.Bob);

        var response = await bob.PostAsync($"/v1/absence-requests/{created.Id}/approve", null, TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Approve_ByAManagerOfAnotherTeam_Returns404()
    {
        var created = await SubmitAndReadAsync(Team.As(_factory.CreateClient(), _team.Alice), weeksAhead: 9);
        var outsider = Team.As(_factory.CreateClient(), _team.Outsider, manager: true);

        var response = await outsider.PostAsync($"/v1/absence-requests/{created.Id}/approve", null, TestContext.Current.CancellationToken);

        // Requests outside your scope are reported as missing, so ids cannot be probed.
        await ShouldBeProblemAsync(response, HttpStatusCode.NotFound, "absence.not_found");
    }

    [Fact]
    public async Task Reject_WithoutReason_Returns400()
    {
        var created = await SubmitAndReadAsync(Team.As(_factory.CreateClient(), _team.Alice), weeksAhead: 10);
        var manager = Team.As(_factory.CreateClient(), _team.Manager, manager: true);

        var response = await manager.PostAsJsonAsync($"/v1/absence-requests/{created.Id}/reject",
            new { reason = "" }, TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Cancel_ThenCancelAgain_Returns409()
    {
        var alice = Team.As(_factory.CreateClient(), _team.Alice);
        var created = await SubmitAndReadAsync(alice, weeksAhead: 11);

        (await alice.PostAsync($"/v1/absence-requests/{created.Id}/cancel", null, TestContext.Current.CancellationToken))
            .StatusCode.ShouldBe(HttpStatusCode.OK);
        var again = await alice.PostAsync($"/v1/absence-requests/{created.Id}/cancel", null, TestContext.Current.CancellationToken);

        await ShouldBeProblemAsync(again, HttpStatusCode.Conflict, "absence.cannot_be_cancelled");
    }

    [Fact]
    public async Task List_AsManager_SeesTheTeamWithPaginationLinks()
    {
        await SubmitAndReadAsync(Team.As(_factory.CreateClient(), _team.Alice), weeksAhead: 12);
        await SubmitAndReadAsync(Team.As(_factory.CreateClient(), _team.Alice), weeksAhead: 13);
        await SubmitAndReadAsync(Team.As(_factory.CreateClient(), _team.Bob), weeksAhead: 12);
        var manager = Team.As(_factory.CreateClient(), _team.Manager, manager: true);

        var response = await manager.GetAsync("/v1/absence-requests?pageSize=2&status=Submitted", TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var page = await response.Content.ReadFromJsonAsync<PagedResponse<AbsenceRequestResponse>>(TestContext.Current.CancellationToken);
        page!.TotalCount.ShouldBe(3);
        page.TotalPages.ShouldBe(2);
        page.Items.Count.ShouldBe(2);
        var link = response.Headers.GetValues("Link").ShouldHaveSingleItem();
        link.ShouldContain("status=Submitted&page=2&pageSize=2>; rel=\"next\"");
    }

    [Fact]
    public async Task List_AsEmployee_SeesOnlyOwnRequests()
    {
        await SubmitAndReadAsync(Team.As(_factory.CreateClient(), _team.Alice), weeksAhead: 14);
        await SubmitAndReadAsync(Team.As(_factory.CreateClient(), _team.Bob), weeksAhead: 14);

        var page = await Team.As(_factory.CreateClient(), _team.Bob)
            .GetFromJsonAsync<PagedResponse<AbsenceRequestResponse>>("/v1/absence-requests", TestContext.Current.CancellationToken);

        page!.Items.ShouldAllBe(r => r.EmployeeId == _team.Bob.Id);
    }

    [Fact]
    public async Task List_InvalidPageSize_Returns400()
    {
        var response = await Team.As(_factory.CreateClient(), _team.Alice)
            .GetAsync("/v1/absence-requests?pageSize=500", TestContext.Current.CancellationToken);

        await ShouldBeProblemAsync(response, HttpStatusCode.BadRequest, "pagination.invalid_page_size");
    }

    [Fact]
    public async Task Get_UnknownId_Returns404Problem()
    {
        var response = await Team.As(_factory.CreateClient(), _team.Alice)
            .GetAsync($"/v1/absence-requests/{Guid.NewGuid()}", TestContext.Current.CancellationToken);

        await ShouldBeProblemAsync(response, HttpStatusCode.NotFound, "absence.not_found");
    }

    public async ValueTask DisposeAsync() => await _factory.DisposeAsync();

    private static SubmitAbsenceRequest Request(int weeksAhead)
    {
        var monday = Dates.Monday(weeksAhead);
        return new SubmitAbsenceRequest(AbsenceType.PaidLeave, monday, monday.AddDays(4), "Holidays");
    }

    private static Task<HttpResponseMessage> SubmitAsync(HttpClient client, SubmitAbsenceRequest request, string? key = "generate")
    {
        var message = new HttpRequestMessage(HttpMethod.Post, "/v1/absence-requests") { Content = JsonContent.Create(request) };
        if (key is not null)
        {
            message.Headers.Add(ApiHeaders.IdempotencyKey, key == "generate" ? Guid.NewGuid().ToString() : key);
        }

        return client.SendAsync(message, TestContext.Current.CancellationToken);
    }

    private static async Task<AbsenceRequestResponse> SubmitAndReadAsync(HttpClient client, int weeksAhead)
    {
        var response = await SubmitAsync(client, Request(weeksAhead));
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<AbsenceRequestResponse>(TestContext.Current.CancellationToken))!;
    }

    private static async Task ShouldBeProblemAsync(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        response.StatusCode.ShouldBe(status);
        response.Content.Headers.ContentType!.MediaType.ShouldBe("application/problem+json");
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>(TestContext.Current.CancellationToken);
        problem!.Extensions["code"]!.ToString().ShouldBe(code);
        problem.Extensions.ShouldContainKey("traceId");
    }
}
