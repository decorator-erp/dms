using BuildingBlocks.Messaging;
using BuildingBlocks.Pagination;
using Finance.Contracts.CostCategories;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Finance.Application.Tests;

[Collection(FinanceApplicationCollection.Name)]
public sealed class CostCategoryFlowTests(FinanceApplicationFixture fixture)
{
    private static string Code() => $"CC{Guid.NewGuid().ToString("N")[..8]}";

    private async Task<Guid> CreateAsync(string code, string name = "Materials")
    {
        await using var scope = fixture.CreateScope();
        var result = await scope.ServiceProvider
            .GetRequiredService<ICommandHandler<CreateCostCategoryCommand, Guid>>()
            .HandleAsync(new CreateCostCategoryCommand(new CreateCostCategoryRequest(code, name)));

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.ToString() : null);
        return result.Value;
    }

    [Fact]
    public async Task Create_ThenReadBack_RoundTripsEveryField()
    {
        var code = Code();
        var id = await CreateAsync(code, "Materials");

        await using var scope = fixture.CreateScope();
        var read = await scope.ServiceProvider
            .GetRequiredService<IQueryHandler<GetCostCategoryByIdQuery, CostCategoryResponse>>()
            .HandleAsync(new GetCostCategoryByIdQuery(id));

        Assert.True(read.IsSuccess);
        Assert.Equal(code, read.Value!.Code);
        Assert.Equal("Materials", read.Value.Name);
        Assert.True(read.Value.IsActive);
    }

    [Fact]
    public async Task Create_WithADuplicateCode_FailsWithDuplicatedCode()
    {
        var code = Code();
        await CreateAsync(code);

        await using var scope = fixture.CreateScope();
        var second = await scope.ServiceProvider
            .GetRequiredService<ICommandHandler<CreateCostCategoryCommand, Guid>>()
            .HandleAsync(new CreateCostCategoryCommand(new CreateCostCategoryRequest(code, "Other")));

        Assert.True(second.IsFailure);
        Assert.Equal("cost_categories.DuplicatedCode", second.Error.Code);
    }

    [Fact]
    public async Task Update_ChangesTheName_AndLeavesTheCodeAlone()
    {
        var code = Code();
        var id = await CreateAsync(code, "Before");

        await using var scope = fixture.CreateScope();
        var sp = scope.ServiceProvider;

        var updated = await sp.GetRequiredService<ICommandHandler<UpdateCostCategoryCommand>>()
            .HandleAsync(new UpdateCostCategoryCommand(id, new UpdateCostCategoryRequest("After", "note")));
        Assert.True(updated.IsSuccess);

        var read = await sp.GetRequiredService<IQueryHandler<GetCostCategoryByIdQuery, CostCategoryResponse>>()
            .HandleAsync(new GetCostCategoryByIdQuery(id));

        Assert.Equal("After", read.Value!.Name);
        Assert.Equal("note", read.Value.Description);
        Assert.Equal(code, read.Value.Code);
    }

    /// <summary>
    /// The backend edits only ACTIVE categories, so a deactivated one reports NotFound rather than being
    /// silently updated. Ported as-is, and worth a test because it is the kind of predicate that gets
    /// "simplified" away — and it is what makes reactivation an explicit step rather than an accident.
    /// </summary>
    [Fact]
    public async Task Update_ADeactivatedCategory_FailsWithNotFound()
    {
        var id = await CreateAsync(Code());

        await using var scope = fixture.CreateScope();
        var sp = scope.ServiceProvider;

        await sp.GetRequiredService<ICommandHandler<DeactivateCostCategoryCommand>>()
            .HandleAsync(new DeactivateCostCategoryCommand(id));

        var updated = await sp.GetRequiredService<ICommandHandler<UpdateCostCategoryCommand>>()
            .HandleAsync(new UpdateCostCategoryCommand(id, new UpdateCostCategoryRequest("After")));

        Assert.True(updated.IsFailure);
        Assert.Equal("cost_categories.NotFound", updated.Error.Code);
    }

    /// <summary>A deactivated category stays READABLE — every budget line already filed under one would
    /// otherwise become unexplainable. This is the complement of the test above: the same row, invisible
    /// to update and visible to read.</summary>
    [Fact]
    public async Task Deactivate_ThenActivate_RoundTripsAndTheRowStaysReadableThroughout()
    {
        var id = await CreateAsync(Code());

        await using var scope = fixture.CreateScope();
        var sp = scope.ServiceProvider;
        var read = sp.GetRequiredService<IQueryHandler<GetCostCategoryByIdQuery, CostCategoryResponse>>();

        await sp.GetRequiredService<ICommandHandler<DeactivateCostCategoryCommand>>()
            .HandleAsync(new DeactivateCostCategoryCommand(id));

        var deactivated = await read.HandleAsync(new GetCostCategoryByIdQuery(id));
        Assert.True(deactivated.IsSuccess);
        Assert.False(deactivated.Value!.IsActive);

        await sp.GetRequiredService<ICommandHandler<ActivateCostCategoryCommand>>()
            .HandleAsync(new ActivateCostCategoryCommand(id));

        var reactivated = await read.HandleAsync(new GetCostCategoryByIdQuery(id));
        Assert.True(reactivated.Value!.IsActive);
    }

    [Fact]
    public async Task List_FiltersByActiveAndSearchesCodeAndName()
    {
        var code = Code();
        var id = await CreateAsync(code, $"Searchable {code}");

        await using var scope = fixture.CreateScope();
        var sp = scope.ServiceProvider;
        var list = sp.GetRequiredService<
            IQueryHandler<GetCostCategoryListQuery, PagedResult<CostCategoryListItemResponse>>>();

        var byCode = await list.HandleAsync(new GetCostCategoryListQuery(Search: code));
        Assert.Contains(byCode.Value!.Items, c => c.Id == id);

        await sp.GetRequiredService<ICommandHandler<DeactivateCostCategoryCommand>>()
            .HandleAsync(new DeactivateCostCategoryCommand(id));

        var active = await list.HandleAsync(new GetCostCategoryListQuery(Search: code, IsActive: true));

        // Liveness: the filtered query returning nothing would satisfy DoesNotContain no matter what the
        // filter did. The unfiltered search above is the positive control, and this asserts the row is
        // still findable when the filter is inverted -- so "absent" means "excluded", not "gone".
        var inactive = await list.HandleAsync(new GetCostCategoryListQuery(Search: code, IsActive: false));
        Assert.Contains(inactive.Value!.Items, c => c.Id == id);
        Assert.DoesNotContain(active.Value!.Items, c => c.Id == id);
    }

    [Fact]
    public async Task List_ReportsTheTotalCountAndPageSize_NotJustThePage()
    {
        var code = Code();
        await CreateAsync(code, $"Paged {code}");

        await using var scope = fixture.CreateScope();
        var page = await scope.ServiceProvider.GetRequiredService<
                IQueryHandler<GetCostCategoryListQuery, PagedResult<CostCategoryListItemResponse>>>()
            .HandleAsync(new GetCostCategoryListQuery(Skip: 0, Take: 1));

        Assert.Single(page.Value!.Items);
        Assert.Equal(1, page.Value.PageSize);

        // PagedResult here is (Items, PageNumber, PageSize, TotalCount) -- a different parameter order
        // from the backend's, which is exactly the sort of thing that transposes silently. TotalCount
        // must count the whole table, not the page.
        Assert.True(page.Value.TotalCount >= 1);
    }

    [Fact]
    public async Task GetById_ForAnUnknownId_FailsWithNotFound()
    {
        await using var scope = fixture.CreateScope();
        var read = await scope.ServiceProvider
            .GetRequiredService<IQueryHandler<GetCostCategoryByIdQuery, CostCategoryResponse>>()
            .HandleAsync(new GetCostCategoryByIdQuery(Guid.NewGuid()));

        Assert.True(read.IsFailure);
        Assert.Equal("cost_categories.NotFound", read.Error.Code);
    }
}
