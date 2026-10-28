using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.Extensions.Options;
using Sample.Api.Contracts;
using Sample.Api.Security;
using Sample.Application.Abstractions;

namespace Sample.Api.Http;

/// <summary>
/// Makes a creation endpoint safe to retry. The key is scoped to the caller and bound to a hash of the request body:
/// same key and same body replays the stored response, same key with a different body is rejected (422), and a
/// second call while the first is still running gets 409.
/// </summary>
internal sealed class IdempotencyFilter<TRequest, TResponse>(IIdempotencyStore store, IOptions<JsonOptions> jsonOptions)
    : IEndpointFilter
{
    private const int MaxKeyLength = 100;

    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(next);

        var http = context.HttpContext;
        var key = http.Request.Headers[ApiHeaders.IdempotencyKey].ToString();
        if (string.IsNullOrEmpty(key))
        {
            return Problems.Create(StatusCodes.Status400BadRequest, ErrorCodes.IdempotencyKeyMissing,
                $"The {ApiHeaders.IdempotencyKey} header is required on this endpoint.");
        }

        if (key.Length > MaxKeyLength || key.Any(char.IsControl))
        {
            return Problems.Create(StatusCodes.Status400BadRequest, ErrorCodes.IdempotencyKeyInvalid,
                $"The {ApiHeaders.IdempotencyKey} header must be printable and at most {MaxKeyLength} characters.");
        }

        var callerId = http.User.ToViewer().EmployeeId;
        var body = context.Arguments.OfType<TRequest>().Single();
        var hash = Convert.ToHexStringLower(
            SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(body, jsonOptions.Value.SerializerOptions)));

        var existing = await store.TryReserveAsync(key, callerId, hash, http.RequestAborted);
        if (existing is not null)
        {
            return Replay(existing, hash, http);
        }

        object? result;
        try
        {
            result = await next(context);
        }
        catch
        {
            await store.ReleaseAsync(key, callerId, CancellationToken.None);
            throw;
        }

        // Typed endpoints return a Results<...> union; look at the result it wraps.
        var outcome = result is INestedHttpResult nested ? nested.Result : result;
        var stored = outcome switch
        {
            Created<TResponse> created => new StoredResponse(
                StatusCodes.Status201Created,
                created.Location,
                JsonSerializer.Serialize(created.Value, jsonOptions.Value.SerializerOptions)),
            ProblemHttpResult { StatusCode: < 500 } problem => new StoredResponse(
                problem.StatusCode,
                null,
                JsonSerializer.Serialize(problem.ProblemDetails, jsonOptions.Value.SerializerOptions)),
            _ => null,
        };

        if (stored is null)
        {
            await store.ReleaseAsync(key, callerId, CancellationToken.None);
        }
        else
        {
            await store.CompleteAsync(key, callerId, stored, CancellationToken.None);
        }

        return result;
    }

    private static IResult Replay(IdempotencyRecord existing, string hash, HttpContext http)
    {
        if (existing.RequestHash != hash)
        {
            return Problems.Create(StatusCodes.Status422UnprocessableEntity, ErrorCodes.IdempotencyKeyReused,
                "This idempotency key was already used with a different request body.");
        }

        if (existing.Response is not { } response)
        {
            return Problems.Create(StatusCodes.Status409Conflict, ErrorCodes.IdempotencyInProgress,
                "A request with this idempotency key is still being processed. Retry later.");
        }

        http.Response.Headers[ApiHeaders.IdempotentReplayed] = "true";
        if (response.Location is not null)
        {
            http.Response.Headers.Location = response.Location;
        }

        var contentType = response.StatusCode >= 400 ? "application/problem+json" : "application/json";
        return TypedResults.Content(response.Body, contentType, statusCode: response.StatusCode);
    }
}
