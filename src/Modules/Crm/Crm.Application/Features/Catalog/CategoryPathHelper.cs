namespace Crm.Application.Features.Catalog;

/// <summary>
/// The materialized-path arithmetic every category-tree write shares: computing a child's path from
/// its parent's, and rewriting a subtree's paths/depths after a move. Extracted once here rather than
/// duplicated between create and move — the two operations compute the same shape of thing (a child
/// path from a parent path), and a move additionally has to redo it for every descendant.
/// </summary>
internal static class CategoryPathHelper
{
    /// <summary>Root = <c>/{ownId}/</c>; a child = <c>{parentPath}{ownId}/</c>.</summary>
    public static string ChildPath(string? parentPath, Guid ownId) =>
        (string.IsNullOrEmpty(parentPath) ? "/" : parentPath) + ownId + "/";

    /// <summary>The new path for a descendant whose old path started with <paramref name="oldPrefix"/>,
    /// rebased onto <paramref name="newPrefix"/> — a plain prefix substring replacement, since a
    /// materialized path never revisits an ancestor id.</summary>
    public static string Rebase(string descendantPath, string oldPrefix, string newPrefix) =>
        newPrefix + descendantPath[oldPrefix.Length..];
}
