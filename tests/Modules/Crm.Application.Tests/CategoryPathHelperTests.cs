using Crm.Application.Features.Catalog;
using Xunit;

namespace Crm.Application.Tests;

/// <summary>Pure-function coverage for the materialized-path arithmetic
/// <c>MoveCrmCatalogCategoryCommandHandler</c>/<c>CreateCrmCatalogCategoryCommandHandler</c> share —
/// no database needed, so this runs regardless of Docker availability.</summary>
public class CategoryPathHelperTests
{
    [Fact]
    public void ChildPath_WithNoParent_IsRootShape()
    {
        var id = Guid.Parse("11111111-1111-1111-1111-111111111111");

        string path = CategoryPathHelper.ChildPath(null, id);

        Assert.Equal($"/{id}/", path);
    }

    [Fact]
    public void ChildPath_WithParent_AppendsOwnId()
    {
        var parentId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var childId = Guid.Parse("22222222-2222-2222-2222-222222222222");
        string parentPath = $"/{parentId}/";

        string path = CategoryPathHelper.ChildPath(parentPath, childId);

        Assert.Equal($"/{parentId}/{childId}/", path);
    }

    [Fact]
    public void ChildPath_WithEmptyStringParentPath_TreatsAsRoot()
    {
        var id = Guid.Parse("11111111-1111-1111-1111-111111111111");

        string path = CategoryPathHelper.ChildPath(string.Empty, id);

        Assert.Equal($"/{id}/", path);
    }

    [Fact]
    public void Rebase_ReplacesOnlyThePrefix_KeepingTheSuffix()
    {
        string oldPrefix = "/A/B/";
        string newPrefix = "/C/";
        string descendantPath = "/A/B/D/E/";

        string rebased = CategoryPathHelper.Rebase(descendantPath, oldPrefix, newPrefix);

        Assert.Equal("/C/D/E/", rebased);
    }

    [Fact]
    public void Rebase_OfTheMovedNodeItself_EqualsTheNewPrefix()
    {
        // The moved node's own path IS the old prefix (no suffix beyond it) - rebasing it should
        // produce exactly the new prefix, no trailing artifact.
        string oldPrefix = "/A/B/";
        string newPrefix = "/C/B/";

        string rebased = CategoryPathHelper.Rebase(oldPrefix, oldPrefix, newPrefix);

        Assert.Equal(newPrefix, rebased);
    }

    [Fact]
    public void Rebase_ToRoot_ProducesRootPrefixedPath()
    {
        string oldPrefix = "/A/B/";
        string newPrefix = "/B/";
        string descendantPath = "/A/B/D/";

        string rebased = CategoryPathHelper.Rebase(descendantPath, oldPrefix, newPrefix);

        Assert.Equal("/B/D/", rebased);
    }
}
