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

        var path = CategoryPathHelper.ChildPath(null, id);

        Assert.Equal($"/{id}/", path);
    }

    [Fact]
    public void ChildPath_WithParent_AppendsOwnId()
    {
        var parentId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var childId = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var parentPath = $"/{parentId}/";

        var path = CategoryPathHelper.ChildPath(parentPath, childId);

        Assert.Equal($"/{parentId}/{childId}/", path);
    }

    [Fact]
    public void ChildPath_WithEmptyStringParentPath_TreatsAsRoot()
    {
        var id = Guid.Parse("11111111-1111-1111-1111-111111111111");

        var path = CategoryPathHelper.ChildPath(string.Empty, id);

        Assert.Equal($"/{id}/", path);
    }

    [Fact]
    public void Rebase_ReplacesOnlyThePrefix_KeepingTheSuffix()
    {
        var oldPrefix = "/A/B/";
        var newPrefix = "/C/";
        var descendantPath = "/A/B/D/E/";

        var rebased = CategoryPathHelper.Rebase(descendantPath, oldPrefix, newPrefix);

        Assert.Equal("/C/D/E/", rebased);
    }

    [Fact]
    public void Rebase_OfTheMovedNodeItself_EqualsTheNewPrefix()
    {
        // The moved node's own path IS the old prefix (no suffix beyond it) - rebasing it should
        // produce exactly the new prefix, no trailing artifact.
        var oldPrefix = "/A/B/";
        var newPrefix = "/C/B/";

        var rebased = CategoryPathHelper.Rebase(oldPrefix, oldPrefix, newPrefix);

        Assert.Equal(newPrefix, rebased);
    }

    [Fact]
    public void Rebase_ToRoot_ProducesRootPrefixedPath()
    {
        var oldPrefix = "/A/B/";
        var newPrefix = "/B/";
        var descendantPath = "/A/B/D/";

        var rebased = CategoryPathHelper.Rebase(descendantPath, oldPrefix, newPrefix);

        Assert.Equal("/B/D/", rebased);
    }
}
