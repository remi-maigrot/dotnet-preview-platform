using System.Security.Claims;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Sample.Api.Contracts;
using Sample.Api.Http;
using Sample.Api.Security;
using Sample.Application.Absences;
using Sample.Application.Common;

namespace Sample.Api.Endpoints;

internal static class AbsenceRequestEndpoints
{
    private const string GetRouteName = "GetAbsenceRequest";

    public static IEndpointRouteBuilder MapAbsenceRequestEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/v1/absence-requests")
            .WithTags("Absence requests")
            .RequireAuthorization()
            .RequireRateLimiting(RateLimitingExtensions.PerUserPolicy);

        group.MapPost("/", SubmitAsync)
            .AddEndpointFilter<IdempotencyFilter<SubmitAbsenceRequest, AbsenceRequestResponse>>()
            .WithName("SubmitAbsenceRequest")
            .WithSummary("Submit an absence request")
            .WithDescription($"Requires an {ApiHeaders.IdempotencyKey} header. Retrying with the same key and body replays the original response.");

        group.MapGet("/", ListAsync)
            .WithName("ListAbsenceRequests")
            .WithSummary("List the absence requests visible to the caller");

        group.MapGet("/{id:guid}", GetAsync)
            .WithName(GetRouteName)
            .WithSummary("Get one absence request");

        group.MapPost("/{id:guid}/approve", ApproveAsync)
            .RequireAuthorization(Policies.Manager)
            .WithName("ApproveAbsenceRequest")
            .WithSummary("Approve a request of a direct report");

        group.MapPost("/{id:guid}/reject", RejectAsync)
            .RequireAuthorization(Policies.Manager)
            .WithName("RejectAbsenceRequest")
            .WithSummary("Reject a request of a direct report");

        group.MapPost("/{id:guid}/cancel", CancelAsync)
            .WithName("CancelAbsenceRequest")
            .WithSummary("Cancel one of your own requests");

        return app;
    }

    private static async Task<Results<Created<AbsenceRequestResponse>, ProblemHttpResult>> SubmitAsync(
        SubmitAbsenceRequest body,
        ClaimsPrincipal user,
        SubmitAbsenceRequestHandler handler,
        LinkGenerator links,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        var command = new SubmitAbsenceRequestCommand(
            user.ToViewer().EmployeeId,
            body.Type!.Value.ToDomain(),
            body.StartDate!.Value,
            body.EndDate!.Value,
            body.Comment);
        var result = await handler.HandleAsync(command, cancellationToken);
        if (!result.IsSuccess)
        {
            return Problems.From(result.Error);
        }

        var location = links.GetUriByName(http, GetRouteName, new { id = result.Value.Id });
        return TypedResults.Created(location, result.Value.ToResponse());
    }

    private static async Task<Results<Ok<PagedResponse<AbsenceRequestResponse>>, ProblemHttpResult>> ListAsync(
        [AsParameters] ListAbsenceRequestsQuery query,
        ClaimsPrincipal user,
        AbsenceRequestQueries queries,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        var page = PageRequest.Create(query.Page, query.PageSize);
        if (!page.IsSuccess)
        {
            return Problems.From(page.Error);
        }

        var filter = new AbsenceRequestFilter(user.ToViewer(), query.Status?.ToDomain(), query.EmployeeId);
        var result = await queries.ListAsync(filter, page.Value, cancellationToken);

        if (PaginationLinks.Build(http.Request, result) is { } link)
        {
            http.Response.Headers.Link = link;
        }

        return TypedResults.Ok(new PagedResponse<AbsenceRequestResponse>(
            [.. result.Items.Select(r => r.ToResponse())],
            result.Page,
            result.PageSize,
            result.TotalCount,
            result.TotalPages));
    }

    private static async Task<Results<Ok<AbsenceRequestResponse>, ProblemHttpResult>> GetAsync(
        Guid id,
        ClaimsPrincipal user,
        AbsenceRequestQueries queries,
        CancellationToken cancellationToken)
    {
        var result = await queries.GetAsync(id, user.ToViewer(), cancellationToken);
        return result.IsSuccess ? TypedResults.Ok(result.Value.ToResponse()) : Problems.From(result.Error);
    }

    private static async Task<Results<Ok<AbsenceRequestResponse>, ProblemHttpResult>> ApproveAsync(
        Guid id,
        ClaimsPrincipal user,
        ChangeAbsenceRequestHandler handler,
        CancellationToken cancellationToken)
    {
        var result = await handler.ApproveAsync(id, user.ToViewer(), cancellationToken);
        return result.IsSuccess ? TypedResults.Ok(result.Value.ToResponse()) : Problems.From(result.Error);
    }

    private static async Task<Results<Ok<AbsenceRequestResponse>, ProblemHttpResult>> RejectAsync(
        Guid id,
        RejectAbsenceRequest body,
        ClaimsPrincipal user,
        ChangeAbsenceRequestHandler handler,
        CancellationToken cancellationToken)
    {
        var result = await handler.RejectAsync(id, user.ToViewer(), body.Reason, cancellationToken);
        return result.IsSuccess ? TypedResults.Ok(result.Value.ToResponse()) : Problems.From(result.Error);
    }

    private static async Task<Results<Ok<AbsenceRequestResponse>, ProblemHttpResult>> CancelAsync(
        Guid id,
        ClaimsPrincipal user,
        ChangeAbsenceRequestHandler handler,
        CancellationToken cancellationToken)
    {
        var result = await handler.CancelAsync(id, user.ToViewer(), cancellationToken);
        return result.IsSuccess ? TypedResults.Ok(result.Value.ToResponse()) : Problems.From(result.Error);
    }
}

internal sealed record ListAbsenceRequestsQuery(
    [FromQuery] int? Page,
    [FromQuery] int? PageSize,
    [FromQuery] AbsenceStatus? Status,
    [FromQuery] Guid? EmployeeId);
