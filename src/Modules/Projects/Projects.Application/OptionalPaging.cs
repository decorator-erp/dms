namespace Projects.Application;

/// <summary>P13/R1-5: opt-in paging for the list reads that returned every row before P13 (the task and comment
/// lists). With neither parameter the query is left whole, so an existing client that never sent them is not
/// silently cut at a page; with either one it takes a page, the other defaulting to <see cref="DefaultPageNumber"/> /
/// <see cref="DefaultPageSize"/>. The validators bound what is sent (1..1,000,000 and 1..200).</summary>
internal static class OptionalPaging
{
    public const int DefaultPageNumber = 1;
    public const int DefaultPageSize = 200;

    /// <summary>The query's page when asked for, else the query unchanged. Call it after the total order.</summary>
    public static IQueryable<T> PageIfAsked<T>(this IQueryable<T> query, int? pageNumber, int? pageSize)
    {
        if (pageNumber is null && pageSize is null)
        {
            return query;
        }

        int size = pageSize ?? DefaultPageSize;
        return query.Skip(((pageNumber ?? DefaultPageNumber) - 1) * size).Take(size);
    }
}
