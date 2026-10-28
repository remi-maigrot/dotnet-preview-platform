using Sample.Application.Common;

namespace Sample.Api.UnitTests.Application;

public sealed class PaginationTests
{
    [Fact]
    public void PageRequest_Defaults_FirstPageOfTwenty()
    {
        var page = PageRequest.Create(null, null).Value;

        page.Page.ShouldBe(1);
        page.PageSize.ShouldBe(PageRequest.DefaultPageSize);
        page.Offset.ShouldBe(0);
    }

    [Fact]
    public void PageRequest_ThirdPage_ComputesOffset() =>
        PageRequest.Create(3, 25).Value.Offset.ShouldBe(50);

    [Theory]
    [InlineData(0, 10, "pagination.invalid_page")]
    [InlineData(1, 0, "pagination.invalid_page_size")]
    [InlineData(1, 101, "pagination.invalid_page_size")]
    public void PageRequest_OutOfRange_Fails(int page, int size, string code) =>
        PageRequest.Create(page, size).Error!.Code.ShouldBe(code);

    [Theory]
    [InlineData(0, 1, 0, false, false)]
    [InlineData(45, 1, 3, false, true)]
    [InlineData(45, 2, 3, true, true)]
    [InlineData(45, 3, 3, true, false)]
    public void PagedResult_Navigation(int total, int page, int totalPages, bool hasPrevious, bool hasNext)
    {
        var result = new PagedResult<int>([], page, 20, total);

        result.TotalPages.ShouldBe(totalPages);
        result.HasPrevious.ShouldBe(hasPrevious);
        result.HasNext.ShouldBe(hasNext);
    }

    [Fact]
    public void PagedResult_Map_KeepsPaging()
    {
        var mapped = new PagedResult<int>([1, 2], 2, 2, 10).Map(i => i * 10);

        mapped.Items.ShouldBe([10, 20]);
        mapped.Page.ShouldBe(2);
        mapped.TotalCount.ShouldBe(10);
    }
}
