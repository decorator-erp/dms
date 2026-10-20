namespace BuildingBlocks.Persistence;

/// <summary>
/// Aug 3.4: user input inside an <c>ILIKE</c> pattern. <c>%</c> and <c>_</c> are wildcards and <c>\</c>
/// is the escape character, so a search for "50%" used to match "500" as well. Every caller passes the
/// result together with <see cref="EscapeCharacter"/>:
/// <c>EF.Functions.ILike(column, pattern, Like.EscapeCharacter)</c>.
/// </summary>
public static class Like
{
    public const string EscapeCharacter = "\\";

    /// <summary>A "contains" pattern matching <paramref name="input"/> literally, or <c>null</c> for
    /// empty or whitespace input, meaning "no filter".</summary>
    public static string? Contains(string? input) =>
        string.IsNullOrWhiteSpace(input)
            ? null
            : $"%{input.Trim().Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_")}%";
}
