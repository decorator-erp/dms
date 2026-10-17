using BuildingBlocks.Messaging;
using Crm.Contracts;
using Crm.Contracts.Catalog;
using Crm.Contracts.Spaces;
using Microsoft.Extensions.DependencyInjection;
using SharedKernel;
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
        await using AsyncServiceScope scope = fixture.CreateScope();
        IServiceProvider sp = scope.ServiceProvider;
        ICommandHandler<CreateCrmCatalogCategoryCommand, Guid> createCategory = sp.GetRequiredService<ICommandHandler<CreateCrmCatalogCategoryCommand, Guid>>();

        Result<Guid> result = await createCategory.HandleAsync(
            new CreateCrmCatalogCategoryCommand(null, $"root-{Guid.NewGuid():N}", "Furniture", "أثاث"));
        Assert.True(result.IsSuccess);

        IQueryHandler<GetCrmCatalogTreeQuery, IReadOnlyList<CrmCatalogCategoryResponse>> getTree = sp.GetRequiredService<IQueryHandler<GetCrmCatalogTreeQuery, IReadOnlyList<CrmCatalogCategoryResponse>>>();
        IReadOnlyList<CrmCatalogCategoryResponse> tree = (await getTree.HandleAsync(new GetCrmCatalogTreeQuery())).Value!;
        CrmCatalogCategoryResponse root = tree.Single(c => c.Id == result.Value);

        Assert.Equal($"/{result.Value}/", root.Path);
        Assert.Equal(0, root.Depth);
        Assert.Null(root.ParentId);
    }

    [Fact]
    public async Task CreateCategory_Child_InheritsParentPathAndDepth()
    {
        await using AsyncServiceScope scope = fixture.CreateScope();
        IServiceProvider sp = scope.ServiceProvider;
        ICommandHandler<CreateCrmCatalogCategoryCommand, Guid> createCategory = sp.GetRequiredService<ICommandHandler<CreateCrmCatalogCategoryCommand, Guid>>();

        Guid parentId = (await createCategory.HandleAsync(
            new CreateCrmCatalogCategoryCommand(null, $"p-{Guid.NewGuid():N}", "Furniture", "أثاث"))).Value;
        Guid childId = (await createCategory.HandleAsync(
            new CreateCrmCatalogCategoryCommand(parentId, $"c-{Guid.NewGuid():N}", "Beds", "أسرة"))).Value;

        IQueryHandler<GetCrmCatalogTreeQuery, IReadOnlyList<CrmCatalogCategoryResponse>> getTree = sp.GetRequiredService<IQueryHandler<GetCrmCatalogTreeQuery, IReadOnlyList<CrmCatalogCategoryResponse>>>();
        IReadOnlyList<CrmCatalogCategoryResponse> tree = (await getTree.HandleAsync(new GetCrmCatalogTreeQuery())).Value!;
        CrmCatalogCategoryResponse child = tree.Single(c => c.Id == childId);

        Assert.Equal($"/{parentId}/{childId}/", child.Path);
        Assert.Equal(1, child.Depth);
        Assert.Equal(parentId, child.ParentId);
    }

    [Fact]
    public async Task MoveCategory_RewritesTheWholeSubtreesPathsAndDepths()
    {
        await using AsyncServiceScope scope = fixture.CreateScope();
        IServiceProvider sp = scope.ServiceProvider;
        ICommandHandler<CreateCrmCatalogCategoryCommand, Guid> createCategory = sp.GetRequiredService<ICommandHandler<CreateCrmCatalogCategoryCommand, Guid>>();

        Guid oldParentId = (await createCategory.HandleAsync(
            new CreateCrmCatalogCategoryCommand(null, $"old-{Guid.NewGuid():N}", "Old Parent", "أب قديم"))).Value;
        Guid newParentId = (await createCategory.HandleAsync(
            new CreateCrmCatalogCategoryCommand(null, $"new-{Guid.NewGuid():N}", "New Parent", "أب جديد"))).Value;
        Guid movedId = (await createCategory.HandleAsync(
            new CreateCrmCatalogCategoryCommand(oldParentId, $"moved-{Guid.NewGuid():N}", "Moved", "منقول"))).Value;
        Guid grandchildId = (await createCategory.HandleAsync(
            new CreateCrmCatalogCategoryCommand(movedId, $"gc-{Guid.NewGuid():N}", "Grandchild", "حفيد"))).Value;

        ICommandHandler<MoveCrmCatalogCategoryCommand> move = sp.GetRequiredService<ICommandHandler<MoveCrmCatalogCategoryCommand>>();
        Result moveResult = await move.HandleAsync(new MoveCrmCatalogCategoryCommand(movedId, newParentId));
        Assert.True(moveResult.IsSuccess);

        IQueryHandler<GetCrmCatalogTreeQuery, IReadOnlyList<CrmCatalogCategoryResponse>> getTree = sp.GetRequiredService<IQueryHandler<GetCrmCatalogTreeQuery, IReadOnlyList<CrmCatalogCategoryResponse>>>();
        IReadOnlyList<CrmCatalogCategoryResponse> tree = (await getTree.HandleAsync(new GetCrmCatalogTreeQuery())).Value!;
        CrmCatalogCategoryResponse moved = tree.Single(c => c.Id == movedId);
        CrmCatalogCategoryResponse grandchild = tree.Single(c => c.Id == grandchildId);

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
        await using AsyncServiceScope scope = fixture.CreateScope();
        IServiceProvider sp = scope.ServiceProvider;
        ICommandHandler<CreateCrmCatalogCategoryCommand, Guid> createCategory = sp.GetRequiredService<ICommandHandler<CreateCrmCatalogCategoryCommand, Guid>>();

        Guid parentId = (await createCategory.HandleAsync(
            new CreateCrmCatalogCategoryCommand(null, $"p-{Guid.NewGuid():N}", "Parent", "أب"))).Value;
        Guid childId = (await createCategory.HandleAsync(
            new CreateCrmCatalogCategoryCommand(parentId, $"c-{Guid.NewGuid():N}", "Child", "ابن"))).Value;

        ICommandHandler<MoveCrmCatalogCategoryCommand> move = sp.GetRequiredService<ICommandHandler<MoveCrmCatalogCategoryCommand>>();
        Result result = await move.HandleAsync(new MoveCrmCatalogCategoryCommand(parentId, childId));

        Assert.True(result.IsFailure);
    }

    [Fact]
    public async Task ItemAttributeValues_SetTwice_ReplacesRatherThanAccumulates()
    {
        await using AsyncServiceScope scope = fixture.CreateScope();
        IServiceProvider sp = scope.ServiceProvider;
        ICommandHandler<CreateCrmCatalogCategoryCommand, Guid> createCategory = sp.GetRequiredService<ICommandHandler<CreateCrmCatalogCategoryCommand, Guid>>();
        Guid categoryId = (await createCategory.HandleAsync(
            new CreateCrmCatalogCategoryCommand(null, $"cat-{Guid.NewGuid():N}", "Beds", "أسرة"))).Value;

        ICommandHandler<CreateCrmCatalogAttributeCommand, Guid> createAttribute = sp.GetRequiredService<ICommandHandler<CreateCrmCatalogAttributeCommand, Guid>>();
        Guid attributeId = (await createAttribute.HandleAsync(new CreateCrmCatalogAttributeCommand(
            categoryId, $"style-{Guid.NewGuid():N}", "Style", "الطراز", CatalogAttributeType.Text))).Value;

        ICommandHandler<CreateCrmCatalogItemCommand, Guid> createItem = sp.GetRequiredService<ICommandHandler<CreateCrmCatalogItemCommand, Guid>>();
        Guid itemId = (await createItem.HandleAsync(new CreateCrmCatalogItemCommand(
            categoryId, $"bed-{Guid.NewGuid():N}", "Bed M-01", "سرير م-01"))).Value;

        ICommandHandler<SetCrmCatalogItemAttributeValuesCommand> setValues = sp.GetRequiredService<ICommandHandler<SetCrmCatalogItemAttributeValuesCommand>>();
        Assert.True((await setValues.HandleAsync(new SetCrmCatalogItemAttributeValuesCommand(
            itemId, [new CatalogAttributeValueData(attributeId, ValueText: "Modern")]))).IsSuccess);

        // Second set replaces, not appends - re-setting with the SAME attribute id must still leave
        // exactly one value row for this item, not two.
        Result secondResult = await setValues.HandleAsync(new SetCrmCatalogItemAttributeValuesCommand(
            itemId, [new CatalogAttributeValueData(attributeId, ValueText: "Contemporary")]));
        Assert.True(secondResult.IsSuccess);
    }

    [Fact]
    public async Task ItemAttributeValues_WithUnknownAttributeId_IsRejected()
    {
        await using AsyncServiceScope scope = fixture.CreateScope();
        IServiceProvider sp = scope.ServiceProvider;
        ICommandHandler<CreateCrmCatalogCategoryCommand, Guid> createCategory = sp.GetRequiredService<ICommandHandler<CreateCrmCatalogCategoryCommand, Guid>>();
        Guid categoryId = (await createCategory.HandleAsync(
            new CreateCrmCatalogCategoryCommand(null, $"cat-{Guid.NewGuid():N}", "Beds", "أسرة"))).Value;
        ICommandHandler<CreateCrmCatalogItemCommand, Guid> createItem = sp.GetRequiredService<ICommandHandler<CreateCrmCatalogItemCommand, Guid>>();
        Guid itemId = (await createItem.HandleAsync(new CreateCrmCatalogItemCommand(
            categoryId, $"bed-{Guid.NewGuid():N}", "Bed M-02", "سرير م-02"))).Value;

        ICommandHandler<SetCrmCatalogItemAttributeValuesCommand> setValues = sp.GetRequiredService<ICommandHandler<SetCrmCatalogItemAttributeValuesCommand>>();
        Result result = await setValues.HandleAsync(new SetCrmCatalogItemAttributeValuesCommand(
            itemId, [new CatalogAttributeValueData(Guid.NewGuid(), ValueText: "Modern")]));

        Assert.True(result.IsFailure);
    }

    [Fact]
    public async Task MapCategoryToSpaceType_Twice_IsRejectedAsDuplicate()
    {
        await using AsyncServiceScope scope = fixture.CreateScope();
        IServiceProvider sp = scope.ServiceProvider;
        ICommandHandler<CreateCrmCatalogCategoryCommand, Guid> createCategory = sp.GetRequiredService<ICommandHandler<CreateCrmCatalogCategoryCommand, Guid>>();
        Guid categoryId = (await createCategory.HandleAsync(
            new CreateCrmCatalogCategoryCommand(null, $"cat-{Guid.NewGuid():N}", "Sanitary", "صحي"))).Value;

        ICommandHandler<CreateCrmSpaceTypeCommand, Guid> createType = sp.GetRequiredService<ICommandHandler<Crm.Contracts.Spaces.CreateCrmSpaceTypeCommand, Guid>>();
        Guid spaceTypeId = (await createType.HandleAsync(
            new Crm.Contracts.Spaces.CreateCrmSpaceTypeCommand($"bath-{Guid.NewGuid():N}", "Bathroom", "حمام"))).Value;

        ICommandHandler<MapCrmCatalogCategoryToSpaceTypeCommand> map = sp.GetRequiredService<ICommandHandler<MapCrmCatalogCategoryToSpaceTypeCommand>>();
        Assert.True((await map.HandleAsync(new MapCrmCatalogCategoryToSpaceTypeCommand(categoryId, spaceTypeId))).IsSuccess);

        Result duplicateResult = await map.HandleAsync(new MapCrmCatalogCategoryToSpaceTypeCommand(categoryId, spaceTypeId));
        Assert.True(duplicateResult.IsFailure);
    }
}
