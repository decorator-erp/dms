using System.Globalization;
using BuildingBlocks.Messaging;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Projects.Contracts;
using SharedKernel;
using Sites.Contracts.Definition;
using Sites.Contracts.Records;
using Sites.Contracts.Surveys;
using Xunit;
using Xunit.Abstractions;
using SiteItemDataType = Sites.Contracts.SiteItemDataType;

namespace Sites.Application.Tests;

/// <summary>
/// <b>S7.</b> The grid save ran its checks row by row: a module validation, a space read, a catalog read
/// and a label probe per row, then two or three value-staging queries per record. A 500-row grid was
/// thousands of round trips inside one open transaction. Set-based, the number of queries is fixed by
/// the shape of the request, not its size.
/// </summary>
[Collection(SitesApplicationCollection.Name)]
public sealed class SiteBulkSaveCommandCountTests(SitesApplicationFixture fixture, ITestOutputHelper output)
{
    private static string Unique(string prefix) => $"{prefix}_{Guid.NewGuid():N}"[..24];

    private async Task<T> ResolveAsync<T>(Func<IServiceProvider, Task<T>> run)
    {
        await using AsyncServiceScope scope = fixture.CreateScope();
        scope.ServiceProvider.GetRequiredService<TestCurrentUser>()
            .Grant(ProjectsPermissions.Projects.ReadAll);

        return await run(scope.ServiceProvider);
    }

    private Task<Result> SendAsync<TCommand>(TCommand command)
        where TCommand : ICommand =>
        ResolveAsync(sp => sp.GetRequiredService<ICommandHandler<TCommand>>().HandleAsync(command));

    private Task<Result<Guid>> SendAsync<TCommand>(TCommand command, bool _)
        where TCommand : ICommand<Guid> =>
        ResolveAsync(sp => sp.GetRequiredService<ICommandHandler<TCommand, Guid>>().HandleAsync(command));

    private sealed record Form(Guid ModuleId, Guid WidthId, Guid LeafId, Guid OptionId, Guid SpaceId, Guid CatalogItemId);

    private async Task<Form> CreateFormAsync()
    {
        Guid moduleId = (await SendAsync(new CreateSiteModuleCommand(
            new CreateSiteModuleRequest(Unique("m"), "Openings", "الفتحات")), true)).Value;

        Guid widthId = (await SendAsync(new CreateSiteItemCommand(
            new CreateSiteItemRequest(
                Unique("width"), "Width", "العرض", SiteItemDataType.Number, "cm",
                MinValue: 1m, MaxValue: 500m)), true)).Value;
        Assert.True((await SendAsync(new AttachSiteItemCommand(
            moduleId, new AttachSiteItemRequest(widthId, IsRequired: true)), true)).IsSuccess);

        Guid leafId = (await SendAsync(new CreateSiteItemCommand(
            new CreateSiteItemRequest(Unique("leaf"), "Leaf", "درفة", SiteItemDataType.Select)), true)).Value;
        Guid optionId = (await SendAsync(new AddSiteItemOptionCommand(
            leafId, new SiteItemOptionRequest("single", "Single", "مفردة")), true)).Value;
        Assert.True((await SendAsync(new AttachSiteItemCommand(
            moduleId, new AttachSiteItemRequest(leafId)), true)).IsSuccess);

        Guid spaceId = await SiteTestData.CreateSpaceInSharedProjectAsync(fixture);

        string suffix = Guid.NewGuid().ToString("N")[..8];
        Guid categoryId = (await SendAsync(new Crm.Contracts.Catalog.CreateCrmCatalogCategoryCommand(
            null, $"cat{suffix}", "Doors", "أبواب"), true)).Value;
        Guid catalogItemId = (await SendAsync(new Crm.Contracts.Catalog.CreateCrmCatalogItemCommand(
            categoryId, $"itm{suffix}", "Flush door", "باب"), true)).Value;

        return new Form(moduleId, widthId, leafId, optionId, spaceId, catalogItemId);
    }

    private async Task<Guid> CreateSiteAsync()
    {
        SiteTestData.Owners owners = await SiteTestData.OwnersAsync(fixture);

        Result<Guid> result = await SendAsync(new CreateSiteCommand(
            new CreateSiteRequest(owners.ProjectId, new SiteUnitDataRequest(Address: "12 Nasr Road")),
            owners.EmployeeId, SiteTestData.OwnerUserId), true);

        Assert.True(result.IsSuccess);
        return result.Value;
    }

    private static BulkSiteRecordRequest Row(Form form, Guid? recordId, int index, decimal width) =>
        new(recordId, form.ModuleId, $"Door {index}", form.SpaceId, form.CatalogItemId, index, null,
        [
            new SiteValueRequest(form.WidthId, ValueNumber: width),
            new SiteValueRequest(form.LeafId, OptionId: form.OptionId),
        ]);

    /// <summary>
    /// A grid of <paramref name="rows"/>: half updates of stored records, half inserts, every row with a
    /// space, a catalog item, a Number and a Select value. Returns the counts of the counted save only.
    /// </summary>
    private async Task<DbCommandCounter.Counts> CountGridSaveAsync(Form form, int rows)
    {
        Guid siteId = await CreateSiteAsync();
        int half = rows / 2;

        // Seed the half that the counted save will UPDATE -- not counted.
        var seed = Enumerable.Range(0, half).Select(i => Row(form, null, i, 90m)).ToList();
        Assert.True((await SendAsync(new BulkSaveSiteRecordsCommand(
            siteId, new BulkSaveSiteRecordsRequest(seed), SiteTestData.OwnerUserId))).IsSuccess);

        List<Guid> stored = await StoredRecordIdsAsync(siteId);
        Assert.Equal(half, stored.Count);

        var grid = new List<BulkSiteRecordRequest>(rows);
        grid.AddRange(stored.Select((id, i) => Row(form, id, i, 95m)));
        grid.AddRange(Enumerable.Range(half, rows - half).Select(i => Row(form, null, i, 100m)));

        DbCommandCounter.Counts counts = DbCommandCounter.Begin();
        Result result;
        try
        {
            result = await SendAsync(new BulkSaveSiteRecordsCommand(
                siteId, new BulkSaveSiteRecordsRequest(grid), SiteTestData.OwnerUserId));
        }
        finally
        {
            DbCommandCounter.End();
        }

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Code : null);
        Assert.Equal(rows, (await StoredRecordIdsAsync(siteId)).Count);
        Assert.Equal(rows * 2, await CountValuesAsync(siteId));
        return counts;
    }

    [Fact]
    public async Task S7_GridSave_IssuesTheSameNumberOfQueriesFor10And500Rows()
    {
        Form form = await CreateFormAsync();

        DbCommandCounter.Counts small = await CountGridSaveAsync(form, 10);
        DbCommandCounter.Counts large = await CountGridSaveAsync(form, BulkSaveLimit);

        output.WriteLine(
            $"10 rows: {small.Queries} queries, {small.SaveBatches} save batches; " +
            $"{BulkSaveLimit} rows: {large.Queries} queries, {large.SaveBatches} save batches.");

        Assert.Equal(small.Queries, large.Queries);

        // SaveChanges is batched by the provider, so its command count rises in steps of a batch, not
        // per row: 500 rows of two values each is a couple of batches, never hundreds.
        Assert.InRange(large.SaveBatches, small.SaveBatches, small.SaveBatches + 3);
    }

    private const int BulkSaveLimit = 500;

    private async Task<List<Guid>> StoredRecordIdsAsync(Guid siteId)
    {
        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();

        await using var command = new NpgsqlCommand(
            $"""
             SELECT "Id" FROM sites.site_records
             WHERE "SiteId"='{siteId}' AND "IsActive" ORDER BY "SortOrder";
             """, connection);

        var ids = new List<Guid>();
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            ids.Add(reader.GetGuid(0));
        }

        return ids;
    }

    private async Task<int> CountValuesAsync(Guid siteId)
    {
        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();

        await using var command = new NpgsqlCommand(
            $"""
             SELECT count(*) FROM sites.site_record_values v
             JOIN sites.site_records r ON r."Id" = v."RecordId"
             WHERE r."SiteId"='{siteId}';
             """, connection);

        return Convert.ToInt32(await command.ExecuteScalarAsync(), CultureInfo.InvariantCulture);
    }
}
