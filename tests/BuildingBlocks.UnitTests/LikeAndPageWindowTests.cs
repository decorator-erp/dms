using BuildingBlocks.Pagination;
using BuildingBlocks.Persistence;
using Xunit;

namespace BuildingBlocks.UnitTests;

/// <summary>Aug 3.4 (B10): user input in an ILIKE pattern is literal, and a page's skip can't
/// overflow.</summary>
public class LikeAndPageWindowTests
{
    [Theory]
    [InlineData("50%", "%50\\%%")]
    [InlineData("a_b", "%a\\_b%")]
    [InlineData("c:\\temp", "%c:\\\\temp%")]
    [InlineData("  plain  ", "%plain%")]
    [InlineData("%_\\", "%\\%\\_\\\\%")]
    public void Contains_escapes_the_wildcards_and_the_escape_character(string input, string expected) =>
        Assert.Equal(expected, Like.Contains(input));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Contains_of_empty_or_whitespace_is_no_filter(string? input) =>
        Assert.Null(Like.Contains(input));

    [Fact]
    public void TrySkip_computes_the_skip_for_a_reachable_page()
    {
        Assert.True(PageWindow.TrySkip(3, 50, out int skip));
        Assert.Equal(100, skip);
    }

    [Fact]
    public void TrySkip_refuses_a_page_past_int_MaxValue_rather_than_wrapping_negative()
    {
        // (int.MaxValue - 1) * 200 wraps to a negative int; in long it is far beyond any table.
        Assert.False(PageWindow.TrySkip(int.MaxValue, 200, out int skip));
        Assert.Equal(0, skip);
    }
}
