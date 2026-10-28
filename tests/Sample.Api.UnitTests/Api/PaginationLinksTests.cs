using Microsoft.AspNetCore.Http;
using Sample.Api.Http;
using Sample.Application.Common;

namespace Sample.Api.UnitTests.Api;

public sealed class PaginationLinksTests
{
    [Fact]
    public void Build_MiddlePage_HasAllRelationsAndKeepsFilters()
    {
        var request = Request("?status=Approved&page=2&pageSize=10");

        var header = PaginationLinks.Build(request, new PagedResult<int>([], 2, 10, 35));

        header.ShouldNotBeNull();
        header.ShouldContain("<https://api.example.test/v1/absence-requests?status=Approved&page=1&pageSize=10>; rel=\"first\"");
        header.ShouldContain("page=1&pageSize=10>; rel=\"prev\"");
        header.ShouldContain("page=3&pageSize=10>; rel=\"next\"");
        header.ShouldContain("page=4&pageSize=10>; rel=\"last\"");
    }

    [Fact]
    public void Build_FirstPage_HasNoPrevious()
    {
        var header = PaginationLinks.Build(Request(string.Empty), new PagedResult<int>([], 1, 10, 35));

        header.ShouldNotBeNull().ShouldNotContain("rel=\"prev\"");
    }

    [Fact]
    public void Build_NoResult_ReturnsNull() =>
        PaginationLinks.Build(Request(string.Empty), new PagedResult<int>([], 1, 10, 0)).ShouldBeNull();

    private static HttpRequest Request(string query)
    {
        var context = new DefaultHttpContext();
        context.Request.Scheme = "https";
        context.Request.Host = new HostString("api.example.test");
        context.Request.Path = "/v1/absence-requests";
        context.Request.QueryString = new QueryString(query);
        return context.Request;
    }
}
