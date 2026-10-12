namespace Plumy.Application.Common;

/// <summary>
/// One page of a list plus the totals a UI paginator needs.
/// </summary>
public sealed record PagedResult<T>(IReadOnlyList<T> Items, int Page, int PageSize, int TotalCount)
{
    // Rounds up: 11 items in pages of 10 → 2 pages
    public int TotalPages => (TotalCount + PageSize - 1) / PageSize;

    public PagedResult<TResult> Map<TResult>(Func<T, TResult> map) =>
        new(Items.Select(map).ToList(), Page, PageSize, TotalCount);
}
