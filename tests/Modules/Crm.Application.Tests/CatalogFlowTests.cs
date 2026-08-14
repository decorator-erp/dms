using BuildingBlocks.Messaging;
using Crm.Contracts;
using Crm.Contracts.Catalog;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Crm.Application.Tests;

/// <summary>
/// G9-11's own verify block, resolved from the real DI graph. Needs Docker; see the standing
/// environment note in PROGRESS.md if this suite could not run.
/// </summary>
[Collection(CrmApplicationCollection.Name)]
public sealed class CatalogFlowTests(CrmApplicationFixture fixture)
{
    [Fact]
    public async Task CreateCategory_Root_GetsRootShapedPath()
    {
        await using var scope = fixture.CreateScope();
        var sp = scope.ServiceProvider;
        var createCategory = sp.GetRequiredService<ICommandHandler<CreateCrmCatalogCategoryCommand, Guid>>();

        var result = await createCategory.HandleAsync(
            new CreateCrmCatalogCategoryCommand(null, $"root-{Guid.NewGuid():N}", "Furniture", "أثاث"));
        Assert.True(result.IsSuccess);

        var getTree = sp.GetRequiredService<IQueryHandler<GetCrmCatalogTreeQuery, IReadOnlyList<CrmCatalogCategoryResponse>>>();
        var tree = (await getTree.HandleAsync(new GetCrmCatalogTreeQuery())).Value!;
        var root = tree.Single(c => c.Id == result.Value);

        Assert.Equal($"/{result.Value}/", root.Path);
        Assert.Equal(0, root.Depth);
        Assert.Null(root.ParentId);
    }

    [Fact]
    public async Task CreateCategory_Child_InheritsParentPathAndDepth()
    {
        await using var scope = fixture.CreateScope();
        var sp = scope.ServiceProvider;
        var createCategory = sp.GetRequiredService<ICommandHandler<CreateCrmCatalogCategoryCommand, Guid>>();

        var parentId = (await createCategory.HandleAsync(
            new CreateCrmCatalogCategoryCommand(null, $"p-{Guid.NewGuid():N}", "Furniture", "أثاث"))).Value;
        var childId = (await createCategory.HandleAsync(
            new CreateCrmCatalogCategoryCommand(parentId, $"c-{Guid.NewGuid():N}", "Beds", "أسرة"))).Value;

        var getTree = sp.GetRequiredService<IQueryHandler<GetCrmCatalogTreeQuery, IReadOnlyList<CrmCatalogCategoryResponse>>>();
        var tree = (await getTree.HandleAsync(new GetCrmCatalogTreeQuery())).Value!;
        var child = tree.Single(c => c.Id == childId);

        Assert.Equal($"/{parentId}/{childId}/", child.Path);
        Assert.Equal(1, child.Depth);
        Assert.Equal(parentId, child.ParentId);
    }

    [Fact]
    public async Task MoveCategory_RewritesTheWholeSubtreesPathsAndDepths()
    {
        await using var scope = fixture.CreateScope();
        var sp = scope.ServiceProvider;
        var createCategory = sp.GetRequiredService<ICommandHandler<CreateCrmCatalogCategoryCommand, Guid>>();

        var oldParentId = (await createCategory.HandleAsync(
            new CreateCrmCatalogCategoryCommand(null, $"old-{Guid.NewGuid():N}", "Old Parent", "أب قديم"))).Value;
        var newParentId = (await createCategory.HandleAsync(
            new CreateCrmCatalogCategoryCommand(null, $"new-{Guid.NewGuid():N}", "New Parent", "أب جديد"))).Value;
        var movedId = (await createCategory.HandleAsync(
            new CreateCrmCatalogCategoryCommand(oldParentId, $"moved-{Guid.NewGuid():N}", "Moved", "منقول"))).Value;
        var grandchildId = (await createCategory.HandleAsync(
            new CreateCrmCatalogCategoryCommand(movedId, $"gc-{Guid.NewGuid():N}", "Grandchild", "حفيد"))).Value;

        var move = sp.GetRequiredService<ICommandHandler<MoveCrmCatalogCategoryCommand>>();
        var moveResult = await move.HandleAsync(new MoveCrmCatalogCategoryCommand(movedId, newParentId));
        Assert.True(moveResult.IsSuccess);

        var getTree = sp.GetRequiredService<IQueryHandler<GetCrmCatalogTreeQuery, IReadOnlyList<CrmCatalogCategoryResponse>>>();
        var tree = (await getTree.HandleAsync(new GetCrmCatalogTreeQuery())).Value!;
        var moved = tree.Single(c => c.Id == movedId);
        var grandchild = tree.Single(c => c.Id == grandchildId);

        Assert.Equal(newParentId, moved.ParentId);
        Assert.Equal($"/{newParentId}/{movedId}/", moved.Path);
        Assert.Equal(1, moved.Depth);
        // The grandchild never changes parent - only its PATH/DEPTH shift with the subtree.
        Assert.Equal($"/{newParentId}/{movedId}/{grandchildId}/", grandchild.Path);
        Assert.Equal(2, grandchild.Depth);
    }

    [Fact]
    public async Task MoveCategory_UnderItsOwnDescendant_IsRejectedAsACycle()
    {
        await using var scope = fixture.CreateScope();
        var sp = scope.ServiceProvider;
        var createCategory = sp.GetRequiredService<ICommandHandler<CreateCrmCatalogCategoryCommand, Guid>>();

        var parentId = (await createCategory.HandleAsync(
            new CreateCrmCatalogCategoryCommand(null, $"p-{Guid.NewGuid():N}", "Parent", "أب"))).Value;
        var childId = (await createCategory.HandleAsync(
            new CreateCrmCatalogCategoryCommand(parentId, $"c-{Guid.NewGuid():N}", "Child", "ابن"))).Value;

        var move = sp.GetRequiredService<ICommandHandler<MoveCrmCatalogCategoryCommand>>();
        var result = await move.HandleAsync(new MoveCrmCatalogCategoryCommand(parentId, childId));

        Assert.True(result.IsFailure);
    }

    [Fact]
    public async Task ItemAttributeValues_SetTwice_ReplacesRatherThanAccumulates()
    {
        await using var scope = fixture.CreateScope();
        var sp = scope.ServiceProvider;
        var createCategory = sp.GetRequiredService<ICommandHandler<CreateCrmCatalogCategoryCommand, Guid>>();
        var categoryId = (await createCategory.HandleAsync(
            new CreateCrmCatalogCategoryCommand(null, $"cat-{Guid.NewGuid():N}", "Beds", "أسرة"))).Value;

        var createAttribute = sp.GetRequiredService<ICommandHandler<CreateCrmCatalogAttributeCommand, Guid>>();
        var attributeId = (await createAttribute.HandleAsync(new CreateCrmCatalogAttributeCommand(
            categoryId, $"style-{Guid.NewGuid():N}", "Style", "الطراز", CatalogAttributeType.Text))).Value;

        var createItem = sp.GetRequiredService<ICommandHandler<CreateCrmCatalogItemCommand, Guid>>();
        var itemId = (await createItem.HandleAsync(new CreateCrmCatalogItemCommand(
            categoryId, $"bed-{Guid.NewGuid():N}", "Bed M-01", "سرير م-01"))).Value;

        var setValues = sp.GetRequiredService<ICommandHandler<SetCrmCatalogItemAttributeValuesCommand>>();
        Assert.True((await setValues.HandleAsync(new SetCrmCatalogItemAttributeValuesCommand(
            itemId, [new CatalogAttributeValueData(attributeId, ValueText: "Modern")]))).IsSuccess);

        // Second set replaces, not appends - re-setting with the SAME attribute id must still leave
        // exactly one value row for this item, not two.
        var secondResult = await setValues.HandleAsync(new SetCrmCatalogItemAttributeValuesCommand(
            itemId, [new CatalogAttributeValueData(attributeId, ValueText: "Contemporary")]));
        Assert.True(secondResult.IsSuccess);
    }

    [Fact]
    public async Task ItemAttributeValues_WithUnknownAttributeId_IsRejected()
    {
        await using var scope = fixture.CreateScope();
        var sp = scope.ServiceProvider;
        var createCategory = sp.GetRequiredService<ICommandHandler<CreateCrmCatalogCategoryCommand, Guid>>();
        var categoryId = (await createCategory.HandleAsync(
            new CreateCrmCatalogCategoryCommand(null, $"cat-{Guid.NewGuid():N}", "Beds", "أسرة"))).Value;
        var createItem = sp.GetRequiredService<ICommandHandler<CreateCrmCatalogItemCommand, Guid>>();
        var itemId = (await createItem.HandleAsync(new CreateCrmCatalogItemCommand(
            categoryId, $"bed-{Guid.NewGuid():N}", "Bed M-02", "سرير م-02"))).Value;

        var setValues = sp.GetRequiredService<ICommandHandler<SetCrmCatalogItemAttributeValuesCommand>>();
        var result = await setValues.HandleAsync(new SetCrmCatalogItemAttributeValuesCommand(
            itemId, [new CatalogAttributeValueData(Guid.NewGuid(), ValueText: "Modern")]));

        Assert.True(result.IsFailure);
    }

    [Fact]
    public async Task MapCategoryToSpaceType_Twice_IsRejectedAsDuplicate()
    {
        await using var scope = fixture.CreateScope();
        var sp = scope.ServiceProvider;
        var createCategory = sp.GetRequiredService<ICommandHandler<CreateCrmCatalogCategoryCommand, Guid>>();
        var categoryId = (await createCategory.HandleAsync(
            new CreateCrmCatalogCategoryCommand(null, $"cat-{Guid.NewGuid():N}", "Sanitary", "صحي"))).Value;

        var createType = sp.GetRequiredService<ICommandHandler<Crm.Contracts.Spaces.CreateCrmSpaceTypeCommand, Guid>>();
        var spaceTypeId = (await createType.HandleAsync(
            new Crm.Contracts.Spaces.CreateCrmSpaceTypeCommand($"bath-{Guid.NewGuid():N}", "Bathroom", "حمام"))).Value;

        var map = sp.GetRequiredService<ICommandHandler<MapCrmCatalogCategoryToSpaceTypeCommand>>();
        Assert.True((await map.HandleAsync(new MapCrmCatalogCategoryToSpaceTypeCommand(categoryId, spaceTypeId))).IsSuccess);

        var duplicateResult = await map.HandleAsync(new MapCrmCatalogCategoryToSpaceTypeCommand(categoryId, spaceTypeId));
        Assert.True(duplicateResult.IsFailure);
    }
}
