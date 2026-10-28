namespace Sample.Api.Contracts;

/// <summary>A page of results. Navigation links are also sent in the RFC 8288 <c>Link</c> header.</summary>
public sealed record PagedResponse<T>(IReadOnlyList<T> Items, int Page, int PageSize, int TotalCount, int TotalPages);
