using BuildingBlocks.Messaging;
using Microsoft.Extensions.DependencyInjection;
using SharedKernel;
using Sites.Contracts.Definition;
using Xunit;

namespace Sites.Application.Tests;

/// <summary>
/// The failure mode this component exists to create: a stale schema means surveyors capture against a
/// form that no longer matches the definition the validator judges submissions by, so a perfectly
/// filled form is rejected for a field the tablet never drew.
///
/// <para>Each test drives a REAL definition write through DI and then re-reads the schema, so what is
/// asserted is "this mutation path invalidates", not "the cache object has an Invalidate method". The
/// invalidation is global by design — items are shared across modules, so a link change can alter how a
/// different module renders — and that is asserted too.</para>
/// </summary>
[Collection(SitesApplicationCollection.Name)]
public sealed class SiteSchemaCacheTests(SitesApplicationFixture fixture)
{
    private static string Unique(string prefix) => $"{prefix}_{Guid.NewGuid():N}"[..24];

    private async Task<T> SendAsync<T>(ICommand<T> _, Func<IServiceProvider, Task<T>> run)
    {
        await using AsyncServiceScope scope = fixture.CreateScope();
        return await run(scope.ServiceProvider);
    }

    private async Task<Guid> CreateModuleAsync(string code)
    {
        await using AsyncServiceScope scope = fixture.CreateScope();
        Result<Guid> result = await scope.ServiceProvider
            .GetRequiredService<ICommandHandler<CreateSiteModuleCommand, Guid>>()
            .HandleAsync(new CreateSiteModuleCommand(new CreateSiteModuleRequest(code, "M", "م")));

        Assert.True(result.IsSuccess);
        return result.Value;
    }

    private async Task<Guid> CreateItemAsync()
    {
        await using AsyncServiceScope scope = fixture.CreateScope();
        Result<Guid> result = await scope.ServiceProvider
            .GetRequiredService<ICommandHandler<CreateSiteItemCommand, Guid>>()
            .HandleAsync(new CreateSiteItemCommand(new CreateSiteItemRequest(
                Unique("it"), "Width", "العرض", Sites.Contracts.SiteItemDataType.Number, "cm")));

        Assert.True(result.IsSuccess);
        return result.Value;
    }

    private async Task<SiteModuleSchemaResponse> SchemaAsync(string code)
    {
        await using AsyncServiceScope scope = fixture.CreateScope();
        Result<SiteModuleSchemaResponse> result = await scope.ServiceProvider
            .GetRequiredService<IQueryHandler<GetSiteModuleSchemaQuery, SiteModuleSchemaResponse>>()
            .HandleAsync(new GetSiteModuleSchemaQuery(code));

        Assert.True(result.IsSuccess);
        return result.Value!;
    }

    private async Task RunAsync<TCommand>(TCommand command)
        where TCommand : ICommand
    {
        await using AsyncServiceScope scope = fixture.CreateScope();
        Result result = await scope.ServiceProvider
            .GetRequiredService<ICommandHandler<TCommand>>()
            .HandleAsync(command);

        Assert.True(result.IsSuccess);
    }

    private async Task AttachAsync(Guid moduleId, Guid itemId, int sortOrder = 0, bool required = false)
    {
        await using AsyncServiceScope scope = fixture.CreateScope();
        Result<Guid> result = await scope.ServiceProvider
            .GetRequiredService<ICommandHandler<AttachSiteItemCommand, Guid>>()
            .HandleAsync(new AttachSiteItemCommand(
                moduleId, new AttachSiteItemRequest(itemId, required, IsVisible: true, sortOrder)));

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task AttachingAField_IsVisibleOnTheVeryNextSchemaRead()
    {
        string code = Unique("cache_a");
        Guid moduleId = await CreateModuleAsync(code);
        Guid itemId = await CreateItemAsync();

        // Warm the cache with the empty form first -- without this the test would pass even if
        // invalidation did nothing, because the first read would be a miss either way.
        Assert.Empty((await SchemaAsync(code)).Fields);

        await AttachAsync(moduleId, itemId);

        Assert.Single((await SchemaAsync(code)).Fields);
    }

    [Fact]
    public async Task DetachingAField_IsGoneOnTheVeryNextSchemaRead()
    {
        string code = Unique("cache_d");
        Guid moduleId = await CreateModuleAsync(code);
        Guid itemId = await CreateItemAsync();
        await AttachAsync(moduleId, itemId);

        Assert.Single((await SchemaAsync(code)).Fields);

        await RunAsync(new DetachSiteItemCommand(moduleId, itemId));

        Assert.Empty((await SchemaAsync(code)).Fields);
    }

    [Fact]
    public async Task ChangingALinksRequiredness_IsVisibleOnTheVeryNextSchemaRead()
    {
        string code = Unique("cache_r");
        Guid moduleId = await CreateModuleAsync(code);
        Guid itemId = await CreateItemAsync();
        await AttachAsync(moduleId, itemId);

        Assert.False((await SchemaAsync(code)).Fields[0].IsRequired);

        await RunAsync(new UpdateSiteLinkCommand(
            moduleId, itemId, new UpdateSiteLinkRequest(IsRequired: true, IsVisible: true, SortOrder: 0)));

        Assert.True((await SchemaAsync(code)).Fields[0].IsRequired);
    }

    [Fact]
    public async Task ReorderingLinks_IsVisibleOnTheVeryNextSchemaRead()
    {
        string code = Unique("cache_o");
        Guid moduleId = await CreateModuleAsync(code);
        Guid first = await CreateItemAsync();
        Guid second = await CreateItemAsync();
        await AttachAsync(moduleId, first, sortOrder: 0);
        await AttachAsync(moduleId, second, sortOrder: 1);

        Assert.Equal(first, (await SchemaAsync(code)).Fields[0].ItemId);

        await RunAsync(new ReorderSiteLinksCommand(moduleId, new ReorderSiteLinksRequest(
            [new SiteLinkOrderRequest(first, 1), new SiteLinkOrderRequest(second, 0)])));

        Assert.Equal(second, (await SchemaAsync(code)).Fields[0].ItemId);
    }

    [Fact]
    public async Task RenamingAnItem_IsVisibleOnTheVeryNextSchemaRead()
    {
        string code = Unique("cache_i");
        Guid moduleId = await CreateModuleAsync(code);
        Guid itemId = await CreateItemAsync();
        await AttachAsync(moduleId, itemId);

        Assert.Equal("Width", (await SchemaAsync(code)).Fields[0].NameEn);

        await RunAsync(new UpdateSiteItemCommand(
            itemId, new UpdateSiteItemRequest("Clear width", "العرض الصافي", null, null, null, null)));

        Assert.Equal("Clear width", (await SchemaAsync(code)).Fields[0].NameEn);
    }

    [Fact]
    public async Task DeactivatingAnItem_RemovesItFromTheVeryNextSchemaRead()
    {
        string code = Unique("cache_x");
        Guid moduleId = await CreateModuleAsync(code);
        Guid itemId = await CreateItemAsync();
        await AttachAsync(moduleId, itemId);

        Assert.Single((await SchemaAsync(code)).Fields);

        await RunAsync(new SetSiteItemActiveCommand(itemId, IsActive: false));

        Assert.Empty((await SchemaAsync(code)).Fields);
    }

    [Fact]
    public async Task UpdatingTheModuleItself_IsVisibleOnTheVeryNextSchemaRead()
    {
        string code = Unique("cache_m");
        Guid moduleId = await CreateModuleAsync(code);

        Assert.Equal("M", (await SchemaAsync(code)).NameEn);

        await RunAsync(new UpdateSiteModuleCommand(
            moduleId, new UpdateSiteModuleRequest("Openings", "الفتحات", null, null, 0)));

        Assert.Equal("Openings", (await SchemaAsync(code)).NameEn);
    }

    /// <summary>
    /// Invalidation is global, and this is why. Items are shared across modules, so editing one item
    /// changes how EVERY module that links it renders — a per-module cache key would look correct and
    /// serve module B a stale field forever.
    /// </summary>
    [Fact]
    public async Task EditingASharedItem_InvalidatesEveryModuleThatLinksIt()
    {
        string codeA = Unique("shared_a");
        string codeB = Unique("shared_b");
        Guid moduleA = await CreateModuleAsync(codeA);
        Guid moduleB = await CreateModuleAsync(codeB);
        Guid itemId = await CreateItemAsync();

        await AttachAsync(moduleA, itemId);
        await AttachAsync(moduleB, itemId);

        // Warm BOTH.
        Assert.Equal("Width", (await SchemaAsync(codeA)).Fields[0].NameEn);
        Assert.Equal("Width", (await SchemaAsync(codeB)).Fields[0].NameEn);

        await RunAsync(new UpdateSiteItemCommand(
            itemId, new UpdateSiteItemRequest("Renamed", "معاد", null, null, null, null)));

        Assert.Equal("Renamed", (await SchemaAsync(codeA)).Fields[0].NameEn);
        Assert.Equal("Renamed", (await SchemaAsync(codeB)).Fields[0].NameEn);
    }

    /// <summary>
    /// A missing module is never cached: it is usually a URL typo, and caching it would keep answering
    /// 404 for the TTL after someone creates the module they just mistyped.
    /// </summary>
    [Fact]
    public async Task AMissingModule_IsNotCachedSoCreatingItIsImmediatelyVisible()
    {
        string code = Unique("late");

        await using (AsyncServiceScope scope = fixture.CreateScope())
        {
            Result<SiteModuleSchemaResponse> miss = await scope.ServiceProvider
                .GetRequiredService<IQueryHandler<GetSiteModuleSchemaQuery, SiteModuleSchemaResponse>>()
                .HandleAsync(new GetSiteModuleSchemaQuery(code));

            Assert.True(miss.IsFailure);
        }

        await CreateModuleAsync(code);

        Assert.Equal(code, (await SchemaAsync(code)).Code);
    }
}
