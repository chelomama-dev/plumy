using Plumy.Application.Common;

namespace Plumy.Application.Tests.Common;

public class PagedResultTests
{
    [Theory]
    [InlineData(0, 10, 0)]
    [InlineData(1, 10, 1)]
    [InlineData(10, 10, 1)]
    [InlineData(11, 10, 2)]
    [InlineData(5, 2, 3)]
    public void TotalPages_RoundsUp(int totalCount, int pageSize, int expected)
    {
        var result = new PagedResult<int>([], Page: 1, pageSize, totalCount);

        Assert.Equal(expected, result.TotalPages);
    }

    [Fact]
    public void Map_TransformsItemsAndKeepsPagingInfo()
    {
        var result = new PagedResult<int>([1, 2], Page: 3, PageSize: 2, TotalCount: 6);

        var mapped = result.Map(i => $"#{i}");

        Assert.Equal(["#1", "#2"], mapped.Items);
        Assert.Equal((3, 2, 6), (mapped.Page, mapped.PageSize, mapped.TotalCount));
    }
}
