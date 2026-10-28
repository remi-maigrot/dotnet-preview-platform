using Microsoft.AspNetCore.Http.HttpResults;
using Sample.Domain.Common;

namespace Sample.Api.Http;

/// <summary>Turns business errors into RFC 9457 problem details with a stable <c>code</c> extension.</summary>
internal static class Problems
{
    public static ProblemHttpResult From(DomainError error)
    {
        ArgumentNullException.ThrowIfNull(error);

        var status = error.Kind switch
        {
            ErrorKind.Validation => StatusCodes.Status400BadRequest,
            ErrorKind.NotFound => StatusCodes.Status404NotFound,
            ErrorKind.Conflict => StatusCodes.Status409Conflict,
            ErrorKind.Forbidden => StatusCodes.Status403Forbidden,
            _ => throw new ArgumentOutOfRangeException(nameof(error), error.Kind, "Unknown error kind."),
        };
        return Create(status, error.Code, error.Description);
    }

    public static ProblemHttpResult Create(int status, string code, string detail) =>
        TypedResults.Problem(
            statusCode: status,
            detail: detail,
            extensions: new Dictionary<string, object?> { ["code"] = code });
}
