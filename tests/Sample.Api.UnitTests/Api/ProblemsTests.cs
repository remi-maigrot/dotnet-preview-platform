using Sample.Api.Http;
using Sample.Domain.Common;

namespace Sample.Api.UnitTests.Api;

public sealed class ProblemsTests
{
    [Theory]
    [InlineData(ErrorKind.Validation, 400)]
    [InlineData(ErrorKind.NotFound, 404)]
    [InlineData(ErrorKind.Conflict, 409)]
    [InlineData(ErrorKind.Forbidden, 403)]
    public void From_MapsKindToStatusAndKeepsTheCode(ErrorKind kind, int status)
    {
        var problem = Problems.From(new DomainError("some.code", "Some description", kind));

        problem.StatusCode.ShouldBe(status);
        problem.ProblemDetails.Detail.ShouldBe("Some description");
        problem.ProblemDetails.Extensions["code"].ShouldBe("some.code");
    }
}
