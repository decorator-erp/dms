namespace BuildingBlocks.Pagination;

/// <summary>
/// Aug 3.4: the rows a page skips, computed in <c>long</c>. <c>(pageNumber - 1) * pageSize</c> in
/// <c>int</c> overflows for a large page number and wraps to a negative skip, which EF rejects (a 500).
/// A page that starts past <c>int.MaxValue</c> rows can only be empty.
/// </summary>
public static class PageWindow
{
    /// <summary><c>false</c> when the page starts beyond anything <c>Skip</c> can reach: answer it with
    /// an empty page and the real total.</summary>
    public static bool TrySkip(int pageNumber, int pageSize, out int skip)
    {
        long rows = ((long)pageNumber - 1) * pageSize;
        skip = rows is >= 0 and <= int.MaxValue ? (int)rows : 0;
        return rows is >= 0 and <= int.MaxValue;
    }
}
