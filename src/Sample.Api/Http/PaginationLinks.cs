using Microsoft.AspNetCore.Http.Extensions;
using Microsoft.AspNetCore.WebUtilities;
using Sample.Application.Common;

namespace Sample.Api.Http;

/// <summary>Builds the RFC 8288 <c>Link</c> header (first, prev, next, last) for a page of results.</summary>
internal static class PaginationLinks
{
    public static string? Build<T>(HttpRequest request, PagedResult<T> page)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(page);

        if (page.TotalPages == 0)
        {
            return null;
        }

        var links = new List<string> { Link(request, 1, page.PageSize, "first") };
        if (page.HasPrevious)
        {
            links.Add(Link(request, page.Page - 1, page.PageSize, "prev"));
        }

        if (page.HasNext)
        {
            links.Add(Link(request, page.Page + 1, page.PageSize, "next"));
        }

        links.Add(Link(request, page.TotalPages, page.PageSize, "last"));
        return string.Join(", ", links);
    }

    private static string Link(HttpRequest request, int page, int pageSize, string rel)
    {
        var query = QueryHelpers.ParseQuery(request.QueryString.Value)
            .Where(q => !q.Key.Equals("page", StringComparison.OrdinalIgnoreCase)
                && !q.Key.Equals("pageSize", StringComparison.OrdinalIgnoreCase))
            .SelectMany(q => q.Value.Select(v => new KeyValuePair<string, string?>(q.Key, v)))
            .Append(new("page", page.ToString(System.Globalization.CultureInfo.InvariantCulture)))
            .Append(new("pageSize", pageSize.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        var url = UriHelper.BuildAbsolute(request.Scheme, request.Host, request.PathBase, request.Path, QueryString.Create(query));
        return $"<{url}>; rel=\"{rel}\"";
    }
}
