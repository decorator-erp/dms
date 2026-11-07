using BuildingBlocks.Messaging;
using Microsoft.Extensions.DependencyInjection;
using SharedKernel;
using Sites.Contracts;
using Sites.Contracts.Definition;
using Sites.Domain;
using Xunit;
// The eight enum pairs are duplicated between Contracts and Domain by construction (see
// SiteEnumMapping). These tests speak the Contracts vocabulary; SiteErrors comes from Domain.
using SiteItemDataType = Sites.Contracts.SiteItemDataType;

namespace Sites.Application.Tests;

/// <summary>
/// The 16 definition commands and 4 queries, resolved from the real DI graph against a real Postgres —
/// so these prove the <c>AddModuleCqrs</c> wiring and the validator/decorator chain, not just the
/// handler bodies.
///
/// <para>This is where the backend's seven governance tests actually land. They drive
/// <c>SiteDomainService</c> through repository mocks, and the thing they gate on
/// (<c>HasCapturedValuesAsync</c>, <c>ModuleHasRecordsAsync</c>) is a database probe — so the
/// "is it locked" half could never be an entity test. <c>Sites.UnitTests</c> owns the shape rules;
/// this owns the probes.</para>
/// </summary>
[Collection(SitesApplicationCollection.Name)]
public sealed class SiteDefinitionFlowTests(SitesApplicationFixture fixture)
{
    private static string Unique(string prefix) => $"{prefix}_{Guid.NewGuid():N}"[..24];

    private async Task<Guid> CreateModuleAsync(string? code = null)
    {
        await using AsyncServiceScope scope = fixture.CreateScope();
        ICommandHandler<CreateSiteModuleCommand, Guid> handler = scope.ServiceProvider
            .GetRequiredService<ICommandHandler<CreateSiteModuleCommand, Guid>>();

        Result<Guid> result = await handler.HandleAsync(new CreateSiteModuleCommand(
            new CreateSiteModuleRequest(code ?? Unique("mod"), "Openings", "الفتحات")));

        Assert.True(result.IsSuccess);
        return result.Value;
    }

    private async Task<Guid> CreateItemAsync(
        SiteItemDataType dataType = SiteItemDataType.Number, string? code = null)
    {
        await using AsyncServiceScope scope = fixture.CreateScope();
        ICommandHandler<CreateSiteItemCommand, Guid> handler = scope.ServiceProvider
            .GetRequiredService<ICommandHandler<CreateSiteItemCommand, Guid>>();

        Result<Guid> result = await handler.HandleAsync(new CreateSiteItemCommand(
            new CreateSiteItemRequest(code ?? Unique("item"), "Width", "العرض", dataType, "cm")));

        Assert.True(result.IsSuccess);
        return result.Value;
    }

    private async Task<Result> SendAsync<TCommand>(TCommand command)
        where TCommand : ICommand
    {
        await using AsyncServiceScope scope = fixture.CreateScope();
        return await scope.ServiceProvider
            .GetRequiredService<ICommandHandler<TCommand>>()
            .HandleAsync(command);
    }

    // ── Codes are unique, case-insensitively ───────────────────────────────────

    [Fact]
    public async Task CreateModule_WithADuplicateCodeInAnotherCase_IsRefused()
    {
        string code = Unique("dup");
        await CreateModuleAsync(code);

        await using AsyncServiceScope scope = fixture.CreateScope();
        ICommandHandler<CreateSiteModuleCommand, Guid> handler = scope.ServiceProvider
            .GetRequiredService<ICommandHandler<CreateSiteModuleCommand, Guid>>();

        Result<Guid> result = await handler.HandleAsync(new CreateSiteModuleCommand(
            new CreateSiteModuleRequest(code.ToUpperInvariant(), "Other", "أخرى")));

        Assert.Equal(SiteErrors.CodeTaken, result.Error);
    }

    [Fact]
    public async Task CreateItem_WithADuplicateCodeInAnotherCase_IsRefused()
    {
        string code = Unique("dupitem");
        await CreateItemAsync(code: code);

        await using AsyncServiceScope scope = fixture.CreateScope();
        ICommandHandler<CreateSiteItemCommand, Guid> handler = scope.ServiceProvider
            .GetRequiredService<ICommandHandler<CreateSiteItemCommand, Guid>>();

        Result<Guid> result = await handler.HandleAsync(new CreateSiteItemCommand(
            new CreateSiteItemRequest(code.ToUpperInvariant(), "W", "ع", SiteItemDataType.Number)));

        Assert.Equal(SiteErrors.CodeTaken, result.Error);
    }

    // ── The frozen-once-captured rule, which needs the database probe ──────────

    [Fact]
    public async Task ChangeItemShape_WithNoCapturedValues_IsAllowed()
    {
        Guid itemId = await CreateItemAsync();

        Result result = await SendAsync(new ChangeSiteItemShapeCommand(
            itemId, new ChangeSiteItemShapeRequest(Unique("reshaped"), SiteItemDataType.Number, "mm", 1m)));

        Assert.True(result.IsSuccess);
    }

    /// <summary>
    /// The paired locked case. The backend's governance tests always pair locked with unlocked so that a
    /// service refusing unconditionally cannot pass; that bar is kept here, where the probe is real.
    /// </summary>
    [Fact]
    public async Task ChangeItemShape_OnceAValueReferencesTheItem_IsRefused()
    {
        Guid itemId = await CreateItemAsync();
        await SiteTestData.CaptureValueAsync(fixture, itemId);

        Result result = await SendAsync(new ChangeSiteItemShapeCommand(
            itemId, new ChangeSiteItemShapeRequest(Unique("nope"), SiteItemDataType.Number, "mm", 1m)));

        Assert.Equal(SiteErrors.ItemLocked, result.Error);
    }

    [Fact]
    public async Task ChangeItemShape_ToItsOwnCode_SkipsTheUniquenessProbe()
    {
        string code = Unique("same");
        Guid itemId = await CreateItemAsync(code: code);

        // An unchanged code must not collide with the item's own row.
        Result result = await SendAsync(new ChangeSiteItemShapeCommand(
            itemId, new ChangeSiteItemShapeRequest(code, SiteItemDataType.Number, "cm", 0.5m)));

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task DeleteItem_OnceAValueReferencesIt_IsRefused()
    {
        Guid itemId = await CreateItemAsync();
        await SiteTestData.CaptureValueAsync(fixture, itemId);

        Result result = await SendAsync(new DeleteSiteItemCommand(itemId));

        Assert.Equal(SiteErrors.ItemInUse, result.Error);
    }

    [Fact]
    public async Task DeleteItem_WhileStillLinkedToAModule_IsRefused()
    {
        Guid moduleId = await CreateModuleAsync();
        Guid itemId = await CreateItemAsync();

        await using (AsyncServiceScope scope = fixture.CreateScope())
        {
            ICommandHandler<AttachSiteItemCommand, Guid> attach = scope.ServiceProvider
                .GetRequiredService<ICommandHandler<AttachSiteItemCommand, Guid>>();
            Assert.True((await attach.HandleAsync(
                new AttachSiteItemCommand(moduleId, new AttachSiteItemRequest(itemId)))).IsSuccess);
        }

        Result result = await SendAsync(new DeleteSiteItemCommand(itemId));

        Assert.Equal(SiteErrors.ItemInUse, result.Error);
    }

    [Fact]
    public async Task DeleteItem_WhenUnusedAndUnlinked_Succeeds()
    {
        Guid itemId = await CreateItemAsync();

        Result result = await SendAsync(new DeleteSiteItemCommand(itemId));

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task DeleteModule_WithCapturedRecords_IsRefused()
    {
        Guid itemId = await CreateItemAsync();
        SiteTestData.CapturedValue context = await SiteTestData.CaptureValueAsync(fixture, itemId);

        Result result = await SendAsync(new DeleteSiteModuleCommand(context.ModuleId));

        Assert.Equal(SiteErrors.ModuleInUse, result.Error);
    }

    /// <summary>
    /// And specifically that a SOFT-DELETED record still blocks the delete. This is the whole reason the
    /// probe ignores query filters: the row is hidden from every read but still holds the RESTRICT
    /// foreign key, so a filtered check would let the domain say yes and Postgres say no — a 500 where
    /// the point of the rule is an actionable 409.
    /// </summary>
    [Fact]
    public async Task DeleteModule_WithOnlySoftDeletedRecords_IsStillRefused()
    {
        Guid itemId = await CreateItemAsync();
        SiteTestData.CapturedValue context = await SiteTestData.CaptureValueAsync(fixture, itemId);
        await SiteTestData.SoftDeleteRecordAsync(fixture, context.RecordId);

        Result result = await SendAsync(new DeleteSiteModuleCommand(context.ModuleId));

        Assert.Equal(SiteErrors.ModuleInUse, result.Error);
    }

    // ── Attach / detach / reattach ─────────────────────────────────────────────

    [Fact]
    public async Task AttachItem_Twice_IsRefusedTheSecondTime()
    {
        Guid moduleId = await CreateModuleAsync();
        Guid itemId = await CreateItemAsync();

        await using AsyncServiceScope scope = fixture.CreateScope();
        ICommandHandler<AttachSiteItemCommand, Guid> attach = scope.ServiceProvider
            .GetRequiredService<ICommandHandler<AttachSiteItemCommand, Guid>>();

        Assert.True((await attach.HandleAsync(
            new AttachSiteItemCommand(moduleId, new AttachSiteItemRequest(itemId)))).IsSuccess);

        Result<Guid> second = await attach.HandleAsync(
            new AttachSiteItemCommand(moduleId, new AttachSiteItemRequest(itemId)));

        Assert.Equal(SiteErrors.AlreadyLinked, second.Error);
    }

    /// <summary>
    /// Detach is a one-way door without this branch. Reattaching must REUSE the existing link row —
    /// reactivating it, so the values captured under it become visible again — rather than inserting a
    /// second link for the same pair, which the unique index would refuse anyway.
    /// </summary>
    [Fact]
    public async Task DetachThenReattach_ReusesTheSameLinkAndReappliesTheFlags()
    {
        Guid moduleId = await CreateModuleAsync();
        Guid itemId = await CreateItemAsync();

        Guid firstLinkId;
        await using (AsyncServiceScope scope = fixture.CreateScope())
        {
            ICommandHandler<AttachSiteItemCommand, Guid> attach = scope.ServiceProvider
                .GetRequiredService<ICommandHandler<AttachSiteItemCommand, Guid>>();
            Result<Guid> created = await attach.HandleAsync(
                new AttachSiteItemCommand(moduleId, new AttachSiteItemRequest(itemId)));
            firstLinkId = created.Value;
        }

        Assert.True((await SendAsync(new DetachSiteItemCommand(moduleId, itemId))).IsSuccess);

        await using (AsyncServiceScope scope = fixture.CreateScope())
        {
            ICommandHandler<AttachSiteItemCommand, Guid> attach = scope.ServiceProvider
                .GetRequiredService<ICommandHandler<AttachSiteItemCommand, Guid>>();
            Result<Guid> reattached = await attach.HandleAsync(new AttachSiteItemCommand(
                moduleId, new AttachSiteItemRequest(itemId, IsRequired: true, IsVisible: false, SortOrder: 4)));

            Assert.True(reattached.IsSuccess);
            Assert.Equal(firstLinkId, reattached.Value);
        }

        SiteModuleSchemaResponse schema = await SchemaAsync(moduleId);
        SiteSchemaFieldResponse field = Assert.Single(schema.Fields);
        Assert.True(field.IsRequired);
        Assert.False(field.IsVisible);
        Assert.Equal(4, field.SortOrder);
    }

    /// <summary>
    /// Defect B16's fix. The backend checks only that the item EXISTS, so a deactivated item attaches,
    /// returns 201, and then never renders — the schema and the pivot both require <c>item.IsActive</c>.
    /// The admin sees success and an empty form field.
    /// </summary>
    [Fact]
    public async Task AttachItem_ThatIsDeactivated_IsRefusedRatherThanSilentlyInvisible()
    {
        Guid moduleId = await CreateModuleAsync();
        Guid itemId = await CreateItemAsync();

        Assert.True((await SendAsync(new SetSiteItemActiveCommand(itemId, IsActive: false))).IsSuccess);

        await using AsyncServiceScope scope = fixture.CreateScope();
        ICommandHandler<AttachSiteItemCommand, Guid> attach = scope.ServiceProvider
            .GetRequiredService<ICommandHandler<AttachSiteItemCommand, Guid>>();

        Result<Guid> result = await attach.HandleAsync(
            new AttachSiteItemCommand(moduleId, new AttachSiteItemRequest(itemId)));

        Assert.Equal(SiteErrors.ItemNotFound, result.Error);
    }

    [Fact]
    public async Task Reorder_WithAnItemThatIsNotLinked_IsRefused()
    {
        Guid moduleId = await CreateModuleAsync();

        Result result = await SendAsync(new ReorderSiteLinksCommand(
            moduleId, new ReorderSiteLinksRequest([new SiteLinkOrderRequest(Guid.NewGuid(), 1)])));

        Assert.Equal(SiteErrors.LinkNotFound, result.Error);
    }

    // ── Options require Select ─────────────────────────────────────────────────

    [Fact]
    public async Task AddOption_ToANonSelectItem_IsRefused()
    {
        Guid itemId = await CreateItemAsync(SiteItemDataType.Number);

        await using AsyncServiceScope scope = fixture.CreateScope();
        ICommandHandler<AddSiteItemOptionCommand, Guid> handler = scope.ServiceProvider
            .GetRequiredService<ICommandHandler<AddSiteItemOptionCommand, Guid>>();

        Result<Guid> result = await handler.HandleAsync(new AddSiteItemOptionCommand(
            itemId, new SiteItemOptionRequest("single", "Single", "مفردة")));

        Assert.Equal(SiteErrors.OptionsRequireSelect, result.Error);
    }

    [Fact]
    public async Task AddOption_ToASelectItem_Succeeds_AndDuplicateCodesAreRefused()
    {
        Guid itemId = await CreateItemAsync(SiteItemDataType.Select);

        await using AsyncServiceScope scope = fixture.CreateScope();
        ICommandHandler<AddSiteItemOptionCommand, Guid> handler = scope.ServiceProvider
            .GetRequiredService<ICommandHandler<AddSiteItemOptionCommand, Guid>>();

        Assert.True((await handler.HandleAsync(new AddSiteItemOptionCommand(
            itemId, new SiteItemOptionRequest("single", "Single", "مفردة")))).IsSuccess);

        Result<Guid> duplicate = await handler.HandleAsync(new AddSiteItemOptionCommand(
            itemId, new SiteItemOptionRequest("SINGLE", "Single again", "مفردة")));

        Assert.Equal(SiteErrors.CodeTaken, duplicate.Error);
    }

    // ── Queries ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Schema_ExposesRequirednessFromTheLinkNotTheItem()
    {
        Guid moduleId = await CreateModuleAsync();
        Guid itemId = await CreateItemAsync();

        await using (AsyncServiceScope scope = fixture.CreateScope())
        {
            ICommandHandler<AttachSiteItemCommand, Guid> attach = scope.ServiceProvider
                .GetRequiredService<ICommandHandler<AttachSiteItemCommand, Guid>>();
            await attach.HandleAsync(new AttachSiteItemCommand(
                moduleId, new AttachSiteItemRequest(itemId, IsRequired: true, SortOrder: 2)));
        }

        SiteModuleSchemaResponse schema = await SchemaAsync(moduleId);
        SiteSchemaFieldResponse field = Assert.Single(schema.Fields);

        Assert.True(field.IsRequired);
        Assert.Equal(itemId, field.ItemId);
        Assert.Equal(2, field.SortOrder);
    }

    [Fact]
    public async Task Schema_ForAnUnknownCode_IsNotFound()
    {
        await using AsyncServiceScope scope = fixture.CreateScope();
        IQueryHandler<GetSiteModuleSchemaQuery, SiteModuleSchemaResponse> handler = scope.ServiceProvider
            .GetRequiredService<IQueryHandler<GetSiteModuleSchemaQuery, SiteModuleSchemaResponse>>();

        Result<SiteModuleSchemaResponse> result = await handler.HandleAsync(new GetSiteModuleSchemaQuery("no-such-module"));

        Assert.Equal(SiteErrors.ModuleNotFound, result.Error);
    }

    [Fact]
    public async Task ItemUsage_ReportsTheModulesAnItemIsLinkedTo()
    {
        Guid moduleId = await CreateModuleAsync();
        Guid itemId = await CreateItemAsync();

        await using (AsyncServiceScope scope = fixture.CreateScope())
        {
            ICommandHandler<AttachSiteItemCommand, Guid> attach = scope.ServiceProvider
                .GetRequiredService<ICommandHandler<AttachSiteItemCommand, Guid>>();
            await attach.HandleAsync(new AttachSiteItemCommand(
                moduleId, new AttachSiteItemRequest(itemId, IsRequired: true)));
        }

        await using AsyncServiceScope queryScope = fixture.CreateScope();
        Result<SiteItemUsageResponse> usage = await queryScope.ServiceProvider
            .GetRequiredService<IQueryHandler<GetSiteItemUsageQuery, SiteItemUsageResponse>>()
            .HandleAsync(new GetSiteItemUsageQuery(itemId));

        Assert.True(usage.IsSuccess);
        Assert.False(usage.Value!.HasCapturedValues);
        Assert.Contains(usage.Value!.Modules, m => m.ModuleId == moduleId && m.IsRequired);
    }

    private async Task<SiteModuleSchemaResponse> SchemaAsync(Guid moduleId)
    {
        await using AsyncServiceScope scope = fixture.CreateScope();

        string code = await SiteTestData.ModuleCodeAsync(fixture, moduleId);

        Result<SiteModuleSchemaResponse> result = await scope.ServiceProvider
            .GetRequiredService<IQueryHandler<GetSiteModuleSchemaQuery, SiteModuleSchemaResponse>>()
            .HandleAsync(new GetSiteModuleSchemaQuery(code));

        Assert.True(result.IsSuccess);
        return result.Value!;
    }

    /// <summary>
    /// <b>B-S-M3.</b> Definition writes translated only xmin conflicts; every unique or foreign-key
    /// violation a race slips past the handlers' pre-checks became <c>SaveFailed</c> (500). Driven here
    /// straight through <c>SiteDefinitionWrites.SaveAsync</c> with the conflicting change staged by hand —
    /// exactly what a request that lost the race between its pre-check and its commit would hold.
    /// </summary>
    [Fact]
    public async Task BSM3_DefinitionSaveViolations_MapTo409And404()
    {
        string code = Unique("dup");
        await CreateModuleAsync(code);
        Guid moduleId = await CreateModuleAsync();
        Guid itemId = await CreateItemAsync();

        // Unique: a second module with the same code (case-insensitive index).
        Assert.Equal(SiteErrors.CodeTaken, await SaveStagedAsync(db =>
            db.SiteModules.Add(Sites.Domain.Definition.SiteModule.Create(code.ToUpperInvariant(), "X", "س").Value!)));

        // Unique: the same item linked twice to one module.
        await using (AsyncServiceScope scope = fixture.CreateScope())
        {
            Result<Guid> attached = await scope.ServiceProvider
                .GetRequiredService<ICommandHandler<AttachSiteItemCommand, Guid>>()
                .HandleAsync(new AttachSiteItemCommand(moduleId, new AttachSiteItemRequest(itemId)));
            Assert.True(attached.IsSuccess);
        }
        Assert.Equal(SiteErrors.AlreadyLinked, await SaveStagedAsync(db =>
            db.SiteModuleItems.Add(Sites.Domain.Definition.SiteModuleItem.Create(moduleId, itemId).Value!)));

        // FK, missing parent: a link to an item that does not exist.
        Assert.Equal(SiteErrors.ItemNotFound, await SaveStagedAsync(db =>
            db.SiteModuleItems.Add(Sites.Domain.Definition.SiteModuleItem.Create(moduleId, Guid.NewGuid()).Value!)));

        // FK, restrict on delete: removing an item still linked to a module.
        Assert.Equal(SiteErrors.ItemInUse, await SaveStagedAsync(db =>
            db.SiteItems.Remove(db.SiteItems.Single(i => i.Id == itemId))));
    }

    private async Task<Error> SaveStagedAsync(Action<Sites.Infrastructure.Persistence.SitesDbContext> stage)
    {
        await using AsyncServiceScope scope = fixture.CreateScope();
        Sites.Infrastructure.Persistence.SitesDbContext db =
            scope.ServiceProvider.GetRequiredService<Sites.Infrastructure.Persistence.SitesDbContext>();
        stage(db);

        Result result = await Sites.Application.Features.Definition.SiteDefinitionWrites.SaveAsync(
            db, scope.ServiceProvider.GetRequiredService<ISiteSchemaCache>(), CancellationToken.None);

        Assert.True(result.IsFailure);
        return result.Error;
    }
}
