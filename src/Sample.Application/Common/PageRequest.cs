using Sample.Domain.Common;

namespace Sample.Application.Common;

public readonly record struct PageRequest
{
    public const int DefaultPageSize = 20;
    public const int MaxPageSize = 100;

    private PageRequest(int page, int pageSize)
    {
        Page = page;
        PageSize = pageSize;
    }

    public int Page { get; }

    public int PageSize { get; }

    public int Offset => (Page - 1) * PageSize;

    public static Result<PageRequest> Create(int? page, int? pageSize)
    {
        var p = page ?? 1;
        var size = pageSize ?? DefaultPageSize;
        if (p < 1)
        {
            return DomainError.Validation("pagination.invalid_page", "The page number starts at 1.");
        }

        if (size is < 1 or > MaxPageSize)
        {
            return DomainError.Validation("pagination.invalid_page_size", $"The page size must be between 1 and {MaxPageSize}.");
        }

        return new PageRequest(p, size);
    }
}
