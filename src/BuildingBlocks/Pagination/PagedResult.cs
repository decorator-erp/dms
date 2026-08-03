namespace BuildingBlocks.Pagination;

/// <summary>
/// Immutable, unlike the ported original: no setters, <see cref="PageSize"/> is exposed (the source
/// dropped it after computing <see cref="TotalPages"/>), and an empty page is a real zero-count
/// instance rather than <c>default!</c>.
/// </summary>
public sealed record PagedResult<T>(IReadOnlyList<T> Items, int PageNumber, int PageSize, int TotalCount)
{
    public int TotalPages => PageSize <= 0 ? 0 : (int)Math.Ceiling(TotalCount / (double)PageSize);

    public static PagedResult<T> Empty(int pageNumber, int pageSize) => new([], pageNumber, pageSize, 0);
}
