using System.Globalization;
using BuildingBlocks.Messaging;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Projects.Contracts;
using SharedKernel;
using Sites.Contracts;
using Sites.Contracts.Definition;
using Sites.Contracts.Documents;
using Sites.Contracts.Records;
using Sites.Contracts.Surveys;
using Sites.Domain;
using Sites.Infrastructure.Persistence;
using Xunit;
using SiteDocumentKind = Sites.Contracts.SiteDocumentKind;
using SiteItemDataType = Sites.Contracts.SiteItemDataType;

namespace Sites.Application.Tests;

/// <summary>
/// Records, values, the grid save and attachments — the rest of the instance layer.
///
/// <para>Write assertions read the row back, for the reason given on <c>SiteLifecycleFlowTests</c>.</para>
/// </summary>
[Collection(SitesApplicationCollection.Name)]
public sealed class SiteRecordFlowTests(SitesApplicationFixture fixture)
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

    private async Task<Guid> CreateSiteAsync()
    {
        SiteTestData.Owners owners = await SiteTestData.OwnersAsync(fixture);

        Result<Guid> result = await SendAsync(new CreateSiteCommand(
            new CreateSiteRequest(owners.ProjectId, new SiteUnitDataRequest(Address: "12 Nasr Road")),
            owners.EmployeeId, SiteTestData.OwnerUserId), true);

        Assert.True(result.IsSuccess);
        return result.Value;
    }

    /// <summary>A module with one required Number field — the smallest form that can be captured.</summary>
    private async Task<(Guid ModuleId, Guid ItemId)> CreateFormAsync()
    {
        Guid moduleId = (await SendAsync(new CreateSiteModuleCommand(
            new CreateSiteModuleRequest(Unique("m"), "Openings", "الفتحات")), true)).Value;

        Guid itemId = (await SendAsync(new CreateSiteItemCommand(
            new CreateSiteItemRequest(
                Unique("width"), "Width", "العرض", SiteItemDataType.Number, "cm",
                MinValue: 1m, MaxValue: 500m)), true)).Value;

        Assert.True((await SendAsync(new AttachSiteItemCommand(
            moduleId, new AttachSiteItemRequest(itemId, IsRequired: true)), true)).IsSuccess);

        return (moduleId, itemId);
    }

    private async Task<int> CountValuesAsync(Guid recordId)
    {
        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();

        await using var command = new NpgsqlCommand(
            $"""SELECT count(*) FROM sites.site_record_values WHERE "RecordId"='{recordId}';""", connection);

        return Convert.ToInt32(await command.ExecuteScalarAsync(), CultureInfo.InvariantCulture);
    }

    private async Task<decimal?> ReadNumberAsync(Guid recordId, Guid itemId)
    {
        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();

        await using var command = new NpgsqlCommand(
            $"""
             SELECT "ValueNumber" FROM sites.site_record_values
             WHERE "RecordId"='{recordId}' AND "ItemId"='{itemId}';
             """, connection);

        return await command.ExecuteScalarAsync() as decimal?;
    }

    private async Task<string?> ReadLabelAsync(Guid recordId)
    {
        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();

        await using var command = new NpgsqlCommand(
            $"""SELECT "Label" FROM sites.site_records WHERE "Id"='{recordId}';""", connection);

        return await command.ExecuteScalarAsync() as string;
    }

    // ── Records ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task AddRecord_PersistsIt()
    {
        Guid siteId = await CreateSiteAsync();
        (Guid moduleId, Guid _) = await CreateFormAsync();

        Result<Guid> result = await SendAsync(new AddSiteRecordCommand(
            siteId, new CreateSiteRecordRequest(moduleId, "Front door"), SiteTestData.OwnerUserId), true);

        Assert.True(result.IsSuccess);
        Assert.Equal("Front door", await ReadLabelAsync(result.Value));
    }

    [Fact]
    public async Task AddRecord_WithADuplicateLabelInAnotherCase_IsRefused()
    {
        Guid siteId = await CreateSiteAsync();
        (Guid moduleId, Guid _) = await CreateFormAsync();

        await SendAsync(new AddSiteRecordCommand(
            siteId, new CreateSiteRecordRequest(moduleId, "Front door"), SiteTestData.OwnerUserId), true);

        Result<Guid> second = await SendAsync(new AddSiteRecordCommand(
            siteId, new CreateSiteRecordRequest(moduleId, "FRONT DOOR"), SiteTestData.OwnerUserId), true);

        Assert.Equal(SiteErrors.LabelTaken, second.Error);
    }

    /// <summary>
    /// A soft-deleted record frees its label, because <c>ux_site_records_label</c> is partial on
    /// <c>IsActive</c>. Without the partiality this would be a constraint violation.
    /// </summary>
    [Fact]
    public async Task DeletingARecord_FreesItsLabelForReuse()
    {
        Guid siteId = await CreateSiteAsync();
        (Guid moduleId, Guid _) = await CreateFormAsync();

        Result<Guid> first = await SendAsync(new AddSiteRecordCommand(
            siteId, new CreateSiteRecordRequest(moduleId, "Front door"), SiteTestData.OwnerUserId), true);

        Assert.True((await SendAsync(new DeleteSiteRecordCommand(
            siteId, first.Value, SiteTestData.OwnerUserId))).IsSuccess);

        Result<Guid> reused = await SendAsync(new AddSiteRecordCommand(
            siteId, new CreateSiteRecordRequest(moduleId, "Front door"), SiteTestData.OwnerUserId), true);

        Assert.True(reused.IsSuccess);
    }

    /// <summary>
    /// The route's site segment is not decoration: a record belonging to another survey answers
    /// <c>RecordNotFound</c>, never a forbidden and never the record.
    /// </summary>
    [Fact]
    public async Task UpdatingARecordThroughTheWrongSite_IsRecordNotFound()
    {
        (Guid moduleId, Guid _) = await CreateFormAsync();
        Guid siteA = await CreateSiteAsync();
        Guid siteB = await CreateSiteAsync();

        Result<Guid> record = await SendAsync(new AddSiteRecordCommand(
            siteA, new CreateSiteRecordRequest(moduleId, "Front door"), SiteTestData.OwnerUserId), true);

        Result result = await SendAsync(new UpdateSiteRecordCommand(
            siteB, record.Value, new UpdateSiteRecordRequest("Renamed", null, null, 0, null),
            SiteTestData.OwnerUserId));

        Assert.Equal(SiteErrors.RecordNotFound, result.Error);
        Assert.Equal("Front door", await ReadLabelAsync(record.Value));
    }

    // ── Values ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task SetValues_PersistsThemAndReplacingWithAnEmptySetClearsThem()
    {
        Guid siteId = await CreateSiteAsync();
        (Guid moduleId, Guid itemId) = await CreateFormAsync();

        Result<Guid> record = await SendAsync(new AddSiteRecordCommand(
            siteId, new CreateSiteRecordRequest(moduleId, "Front door"), SiteTestData.OwnerUserId), true);

        Assert.True((await SendAsync(new SetSiteRecordValuesCommand(
            siteId, record.Value,
            new SetSiteRecordValuesRequest([new SiteValueRequest(itemId, ValueNumber: 90m)]),
            SiteTestData.OwnerUserId))).IsSuccess);

        Assert.Equal(1, await CountValuesAsync(record.Value));

        // Absent means cleared -- the table has no soft-delete flag, and a cleared measurement is not a
        // measurement. The required-field rule still applies, so an empty set is refused...
        Result cleared = await SendAsync(new SetSiteRecordValuesCommand(
            siteId, record.Value, new SetSiteRecordValuesRequest([]), SiteTestData.OwnerUserId));

        Assert.True(cleared.IsFailure);
        Assert.Equal(1, await CountValuesAsync(record.Value));
    }

    [Fact]
    public async Task SetValues_OutsideTheItemsBounds_IsRefusedAndNothingIsWritten()
    {
        Guid siteId = await CreateSiteAsync();
        (Guid moduleId, Guid itemId) = await CreateFormAsync();

        Result<Guid> record = await SendAsync(new AddSiteRecordCommand(
            siteId, new CreateSiteRecordRequest(moduleId, "Front door"), SiteTestData.OwnerUserId), true);

        Result result = await SendAsync(new SetSiteRecordValuesCommand(
            siteId, record.Value,
            new SetSiteRecordValuesRequest([new SiteValueRequest(itemId, ValueNumber: 9999m)]),
            SiteTestData.OwnerUserId));

        Assert.True(result.IsFailure);
        Assert.Equal(0, await CountValuesAsync(record.Value));
    }

    /// <summary>Every failure at once, never the first — the caller is a grid.</summary>
    [Fact]
    public async Task SetValues_ReportsEveryProblemAtOnce()
    {
        Guid siteId = await CreateSiteAsync();
        (Guid moduleId, Guid itemId) = await CreateFormAsync();

        Result<Guid> record = await SendAsync(new AddSiteRecordCommand(
            siteId, new CreateSiteRecordRequest(moduleId, "Front door"), SiteTestData.OwnerUserId), true);

        Result result = await SendAsync(new SetSiteRecordValuesCommand(
            siteId, record.Value,
            new SetSiteRecordValuesRequest(
            [
                new SiteValueRequest(itemId, ValueNumber: 9999m),
                new SiteValueRequest(Guid.NewGuid(), ValueNumber: 1m),
            ]),
            SiteTestData.OwnerUserId));

        ValidationError validation = Assert.IsType<ValidationError>(result.Error);
        Assert.True(validation.Errors.Length >= 2);
    }

    [Fact]
    public async Task SetValues_OnAnApprovedSurvey_IsRefused()
    {
        Guid siteId = await CreateSiteAsync();
        (Guid moduleId, Guid itemId) = await CreateFormAsync();
        SiteTestData.Owners owners = await SiteTestData.OwnersAsync(fixture);

        Result<Guid> record = await SendAsync(new AddSiteRecordCommand(
            siteId, new CreateSiteRecordRequest(moduleId, "Front door"), SiteTestData.OwnerUserId), true);

        await SendAsync(new SetSiteRecordValuesCommand(
            siteId, record.Value,
            new SetSiteRecordValuesRequest([new SiteValueRequest(itemId, ValueNumber: 90m)]),
            SiteTestData.OwnerUserId));

        await SendAsync(new SubmitSiteCommand(siteId, SiteTestData.OwnerEmployeeId, SiteTestData.OwnerUserId));
        await SendAsync(new DecideSiteCommand(
            siteId, new DecideSiteRequest(Approved: true), owners.EmployeeId, SiteTestData.OwnerUserId));

        Result result = await SendAsync(new SetSiteRecordValuesCommand(
            siteId, record.Value,
            new SetSiteRecordValuesRequest([new SiteValueRequest(itemId, ValueNumber: 120m)]),
            SiteTestData.OwnerUserId));

        Assert.Equal(SiteErrors.SiteLocked, result.Error);
        Assert.Equal(1, await CountValuesAsync(record.Value));
    }

    /// <summary>
    /// <b>B12's fix, decided at P5F-02 — the divergence from the backend.</b> The backend freezes
    /// <c>Approved</c> only, so this exact sequence succeeded there: submit for approval, then change the
    /// measurement while the approver is looking at it. The approval would then attest to 120, having
    /// been asked about 90, and <c>ck_sites_status_stamps</c> would record who approved and when with
    /// nothing recording what they saw.
    ///
    /// <para>Asserts the PERSISTED value, not just the returned <c>Result</c> — a handler that returns
    /// the right error after writing the row is still broken, and that is precisely the failure this test
    /// exists to catch.</para>
    /// </summary>
    [Fact]
    public async Task SetValues_OnASubmittedSurvey_IsRefusedAndTheStoredValueIsUnchanged()
    {
        Guid siteId = await CreateSiteAsync();
        (Guid moduleId, Guid itemId) = await CreateFormAsync();

        Result<Guid> record = await SendAsync(new AddSiteRecordCommand(
            siteId, new CreateSiteRecordRequest(moduleId, "Front door"), SiteTestData.OwnerUserId), true);

        await SendAsync(new SetSiteRecordValuesCommand(
            siteId, record.Value,
            new SetSiteRecordValuesRequest([new SiteValueRequest(itemId, ValueNumber: 90m)]),
            SiteTestData.OwnerUserId));

        Assert.True((await SendAsync(new SubmitSiteCommand(siteId, SiteTestData.OwnerEmployeeId, SiteTestData.OwnerUserId))).IsSuccess);

        Result result = await SendAsync(new SetSiteRecordValuesCommand(
            siteId, record.Value,
            new SetSiteRecordValuesRequest([new SiteValueRequest(itemId, ValueNumber: 120m)]),
            SiteTestData.OwnerUserId));

        Assert.Equal(SiteErrors.SiteLocked, result.Error);
        Assert.Equal(409, result.Error.Status);
        Assert.Equal(90m, await ReadNumberAsync(record.Value, itemId));
    }

    /// <summary>Adding a whole record to a submitted survey is refused by the same guard — the freeze is
    /// on the survey, not on one write path.</summary>
    [Fact]
    public async Task AddRecord_OnASubmittedSurvey_IsRefused()
    {
        Guid siteId = await CreateSiteAsync();
        (Guid moduleId, Guid _) = await CreateFormAsync();

        Assert.True((await SendAsync(new SubmitSiteCommand(siteId, SiteTestData.OwnerEmployeeId, SiteTestData.OwnerUserId))).IsSuccess);

        Result<Guid> result = await SendAsync(new AddSiteRecordCommand(
            siteId, new CreateSiteRecordRequest(moduleId, "Added after review"),
            SiteTestData.OwnerUserId), true);

        Assert.Equal(SiteErrors.SiteLocked, result.Error);
    }

    // ── The grid save ──────────────────────────────────────────────────────────

    [Fact]
    public async Task BulkSave_CreatesAndUpdatesInOneTransaction()
    {
        Guid siteId = await CreateSiteAsync();
        (Guid moduleId, Guid itemId) = await CreateFormAsync();

        Result<Guid> existing = await SendAsync(new AddSiteRecordCommand(
            siteId, new CreateSiteRecordRequest(moduleId, "Door 1"), SiteTestData.OwnerUserId), true);

        Result result = await SendAsync(new BulkSaveSiteRecordsCommand(
            siteId,
            new BulkSaveSiteRecordsRequest(
            [
                new BulkSiteRecordRequest(
                    existing.Value, moduleId, "Door 1 renamed", null, null, 0, null,
                    [new SiteValueRequest(itemId, ValueNumber: 90m)]),
                new BulkSiteRecordRequest(
                    null, moduleId, "Door 2", null, null, 1, null,
                    [new SiteValueRequest(itemId, ValueNumber: 95m)]),
            ]),
            SiteTestData.OwnerUserId));

        Assert.True(result.IsSuccess);

        // The create half is the one the backend could not do at all: its value pass QUERIED for the
        // record, so a newly added one answered RecordNotFound and rolled the whole grid back.
        Assert.Equal("Door 1 renamed", await ReadLabelAsync(existing.Value));
        Assert.Equal(1, await CountValuesAsync(existing.Value));
    }

    /// <summary>
    /// <b>Defect B2.</b> The backend validates each row against the CLIENT's <c>ModuleId</c> and then
    /// writes against the stored record's module, because a record cannot change module. So an update row
    /// could name a module with no required fields and skip required-field validation entirely. Here the
    /// module is resolved from the stored record, so the required field is still enforced.
    /// </summary>
    [Fact]
    public async Task BulkSave_AnUpdateRowNamingAnotherModule_IsStillValidatedAgainstItsOwn()
    {
        Guid siteId = await CreateSiteAsync();
        (Guid moduleId, Guid itemId) = await CreateFormAsync();

        // A second module with NO required fields -- the schema an attacker would name to skip validation.
        Guid emptyModuleId = (await SendAsync(new CreateSiteModuleCommand(
            new CreateSiteModuleRequest(Unique("empty"), "Empty", "فارغ")), true)).Value;
        Guid filler = (await SendAsync(new CreateSiteItemCommand(
            new CreateSiteItemRequest(Unique("note"), "Note", "ملاحظة", SiteItemDataType.Text)), true)).Value;
        await SendAsync(new AttachSiteItemCommand(
            emptyModuleId, new AttachSiteItemRequest(filler)), true);

        Result<Guid> record = await SendAsync(new AddSiteRecordCommand(
            siteId, new CreateSiteRecordRequest(moduleId, "Door 1"), SiteTestData.OwnerUserId), true);

        // Claims the empty module, supplies no value for the real module's REQUIRED field.
        Result result = await SendAsync(new BulkSaveSiteRecordsCommand(
            siteId,
            new BulkSaveSiteRecordsRequest(
            [
                new BulkSiteRecordRequest(
                    record.Value, emptyModuleId, "Door 1", null, null, 0, null, []),
            ]),
            SiteTestData.OwnerUserId));

        Assert.True(result.IsFailure);
        ValidationError validation = Assert.IsType<ValidationError>(result.Error);
        Assert.Contains(validation.Errors, e => e.Code.EndsWith(".Required", StringComparison.Ordinal));

        _ = itemId;
    }

    /// <summary>
    /// <b>Defect B3.</b> Two rows for one module, differing only in the <c>ModuleId</c> they claim, used
    /// to land in different dedupe groups and then collide on the unique index as an unhandled write
    /// failure. Keyed on the resolved module, this is a clean <c>LabelTaken</c>.
    /// </summary>
    [Fact]
    public async Task BulkSave_TwoRowsClaimingOneLabel_IsLabelTakenRatherThanAWriteFailure()
    {
        Guid siteId = await CreateSiteAsync();
        (Guid moduleId, Guid itemId) = await CreateFormAsync();

        Result result = await SendAsync(new BulkSaveSiteRecordsCommand(
            siteId,
            new BulkSaveSiteRecordsRequest(
            [
                new BulkSiteRecordRequest(null, moduleId, "Door", null, null, 0, null,
                    [new SiteValueRequest(itemId, ValueNumber: 90m)]),
                new BulkSiteRecordRequest(null, moduleId, "DOOR", null, null, 1, null,
                    [new SiteValueRequest(itemId, ValueNumber: 95m)]),
            ]),
            SiteTestData.OwnerUserId));

        Assert.Equal(SiteErrors.LabelTaken, result.Error);
    }

    /// <summary>All or nothing: a bad row late in the grid must leave no earlier row written.</summary>
    [Fact]
    public async Task BulkSave_WithAFailingRowLate_WritesNothing()
    {
        Guid siteId = await CreateSiteAsync();
        (Guid moduleId, Guid itemId) = await CreateFormAsync();

        Result result = await SendAsync(new BulkSaveSiteRecordsCommand(
            siteId,
            new BulkSaveSiteRecordsRequest(
            [
                new BulkSiteRecordRequest(null, moduleId, "Good door", null, null, 0, null,
                    [new SiteValueRequest(itemId, ValueNumber: 90m)]),
                new BulkSiteRecordRequest(null, moduleId, "Bad door", null, null, 1, null,
                    [new SiteValueRequest(itemId, ValueNumber: 9999m)]),
            ]),
            SiteTestData.OwnerUserId));

        Assert.True(result.IsFailure);

        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            $"""SELECT count(*) FROM sites.site_records WHERE "SiteId"='{siteId}' AND "Label" LIKE '%door';""",
            connection);

        Assert.Equal(0, Convert.ToInt32(await command.ExecuteScalarAsync(), CultureInfo.InvariantCulture));
    }

    // ── Space validation, through ICrmSpaceReader ──────────────────────────────

    /// <summary>
    /// The rule <c>ICrmSpaceReader</c> exists for. A foreign key proves the space row exists; only this
    /// proves it belongs to the same project as the survey. The space here is real but belongs to another
    /// project, which is precisely the case an FK cannot catch.
    /// </summary>
    [Fact]
    public async Task AddRecord_WithASpaceFromAnotherProject_IsSpaceNotInProject()
    {
        Guid siteId = await CreateSiteAsync();
        (Guid moduleId, Guid _) = await CreateFormAsync();

        Guid foreignSpaceId = await SiteTestData.CreateSpaceInAnotherProjectAsync(fixture);

        Result<Guid> result = await SendAsync(new AddSiteRecordCommand(
            siteId, new CreateSiteRecordRequest(moduleId, "Bedroom door", SpaceId: foreignSpaceId),
            SiteTestData.OwnerUserId), true);

        Assert.Equal(SiteErrors.SpaceNotInProject, result.Error);
    }

    [Fact]
    public async Task AddRecord_WithANonexistentSpace_IsSpaceNotFound()
    {
        Guid siteId = await CreateSiteAsync();
        (Guid moduleId, Guid _) = await CreateFormAsync();

        Result<Guid> result = await SendAsync(new AddSiteRecordCommand(
            siteId, new CreateSiteRecordRequest(moduleId, "Bedroom door", SpaceId: Guid.NewGuid()),
            SiteTestData.OwnerUserId), true);

        // The backend collapses this into SpaceNotInProject; ICrmSpaceReader returning the row lets the
        // two cases be told apart.
        Assert.Equal(SiteErrors.SpaceNotFound, result.Error);
    }

    // ── Attachments ────────────────────────────────────────────────────────────

    /// <summary>
    /// <b>Defect B5.</b> The backend never checks the document at all, so a caller with
    /// <c>sites.update</c> on one project could link another project's document and read the id back.
    /// </summary>
    [Fact]
    public async Task LinkDocument_FromAnotherProject_IsDocumentNotFound()
    {
        Guid siteId = await CreateSiteAsync();

        Guid foreignDocumentId = await SiteTestData.CreateDocumentInAnotherProjectAsync(fixture);

        Result<Guid> result = await SendAsync(new LinkSiteDocumentCommand(
            siteId, new LinkSiteDocumentRequest(foreignDocumentId, SiteDocumentKind.Photo),
            (await SiteTestData.OwnersAsync(fixture)).EmployeeId, SiteTestData.OwnerUserId), true);

        Assert.Equal(SiteErrors.DocumentNotFound, result.Error);
    }

    [Fact]
    public async Task LinkDocument_ThatDoesNotExist_IsDocumentNotFound()
    {
        Guid siteId = await CreateSiteAsync();

        Result<Guid> result = await SendAsync(new LinkSiteDocumentCommand(
            siteId, new LinkSiteDocumentRequest(Guid.NewGuid(), SiteDocumentKind.Photo),
            (await SiteTestData.OwnersAsync(fixture)).EmployeeId, SiteTestData.OwnerUserId), true);

        Assert.Equal(SiteErrors.DocumentNotFound, result.Error);
    }

    /// <summary>
    /// <b>Defect B15.</b> The backend answers 400 for a record belonging to another site and 404 for one
    /// that does not exist — an oracle distinguishing "exists but isn't yours" from "doesn't exist", which
    /// its own unlink handler deliberately avoids. Both are <c>RecordNotFound</c> here.
    /// </summary>
    [Fact]
    public async Task LinkDocument_ToAnotherSitesRecord_IsTheSameErrorAsANonexistentRecord()
    {
        (Guid moduleId, Guid _) = await CreateFormAsync();
        Guid siteA = await CreateSiteAsync();
        Guid siteB = await CreateSiteAsync();
        SiteTestData.Owners owners = await SiteTestData.OwnersAsync(fixture);

        Result<Guid> foreignRecord = await SendAsync(new AddSiteRecordCommand(
            siteA, new CreateSiteRecordRequest(moduleId, "Door"), SiteTestData.OwnerUserId), true);

        Guid documentId = await SiteTestData.CreateDocumentAsync(fixture);

        Result<Guid> wrongSite = await SendAsync(new LinkSiteDocumentCommand(
            siteB, new LinkSiteDocumentRequest(documentId, SiteDocumentKind.Photo, foreignRecord.Value),
            owners.EmployeeId, SiteTestData.OwnerUserId), true);

        Result<Guid> missing = await SendAsync(new LinkSiteDocumentCommand(
            siteB, new LinkSiteDocumentRequest(documentId, SiteDocumentKind.Photo, Guid.NewGuid()),
            owners.EmployeeId, SiteTestData.OwnerUserId), true);

        Assert.Equal(SiteErrors.RecordNotFound, wrongSite.Error);
        Assert.Equal(missing.Error, wrongSite.Error);
    }

    [Fact]
    public async Task LinkThenListDocuments_ReturnsTheSitesOwnAndItsRecordsAttachments()
    {
        Guid siteId = await CreateSiteAsync();
        (Guid moduleId, Guid _) = await CreateFormAsync();
        SiteTestData.Owners owners = await SiteTestData.OwnersAsync(fixture);

        Result<Guid> record = await SendAsync(new AddSiteRecordCommand(
            siteId, new CreateSiteRecordRequest(moduleId, "Door"), SiteTestData.OwnerUserId), true);

        Guid siteDocument = await SiteTestData.CreateDocumentAsync(fixture);
        Guid recordDocument = await SiteTestData.CreateDocumentAsync(fixture);

        Assert.True((await SendAsync(new LinkSiteDocumentCommand(
            siteId, new LinkSiteDocumentRequest(siteDocument, SiteDocumentKind.Sketch),
            owners.EmployeeId, SiteTestData.OwnerUserId), true)).IsSuccess);

        Assert.True((await SendAsync(new LinkSiteDocumentCommand(
            siteId, new LinkSiteDocumentRequest(recordDocument, SiteDocumentKind.Photo, record.Value),
            owners.EmployeeId, SiteTestData.OwnerUserId), true)).IsSuccess);

        Result<IReadOnlyList<SiteDocumentResponse>> listed = await ResolveAsync(sp => sp
            .GetRequiredService<IQueryHandler<GetSiteDocumentsQuery, IReadOnlyList<SiteDocumentResponse>>>()
            .HandleAsync(new GetSiteDocumentsQuery(siteId, SiteTestData.OwnerUserId)));

        Assert.True(listed.IsSuccess);
        Assert.Equal(2, listed.Value!.Count);
        Assert.Contains(listed.Value!, d => d.SiteId == siteId && d.RecordId is null);
        Assert.Contains(listed.Value!, d => d.RecordId == record.Value && d.SiteId is null);
    }

    // ── Phase 1 fixes ──────────────────────────────────────────────────────────

    private Task<Result<IReadOnlyList<SiteDocumentResponse>>> ListDocumentsAsync(Guid siteId) =>
        ResolveAsync(sp => sp
            .GetRequiredService<IQueryHandler<GetSiteDocumentsQuery, IReadOnlyList<SiteDocumentResponse>>>()
            .HandleAsync(new GetSiteDocumentsQuery(siteId, SiteTestData.OwnerUserId)));

    /// <summary>
    /// <b>S1.</b> Unlink is a soft delete, but nothing filtered <c>IsActive</c>: the unlinked attachment
    /// still listed, and unlinking it again "succeeded".
    /// </summary>
    [Fact]
    public async Task S1_UnlinkedDocument_IsNotListedAndASecondUnlinkIsDocumentNotFound()
    {
        Guid siteId = await CreateSiteAsync();
        SiteTestData.Owners owners = await SiteTestData.OwnersAsync(fixture);
        Guid documentId = await SiteTestData.CreateDocumentAsync(fixture);

        Result<Guid> link = await SendAsync(new LinkSiteDocumentCommand(
            siteId, new LinkSiteDocumentRequest(documentId, SiteDocumentKind.Sketch),
            owners.EmployeeId, SiteTestData.OwnerUserId), true);
        Assert.True(link.IsSuccess);

        Assert.True((await SendAsync(new UnlinkSiteDocumentCommand(
            siteId, link.Value, SiteTestData.OwnerUserId))).IsSuccess);

        Result<IReadOnlyList<SiteDocumentResponse>> listed = await ListDocumentsAsync(siteId);
        Assert.True(listed.IsSuccess);
        Assert.Empty(listed.Value!);

        Result again = await SendAsync(new UnlinkSiteDocumentCommand(
            siteId, link.Value, SiteTestData.OwnerUserId));
        Assert.Equal(SiteErrors.DocumentNotFound, again.Error);
    }

    /// <summary>
    /// <b>NS2.</b> Nothing stopped one document being linked to the same subject twice, so the gallery
    /// showed it twice. Linking it to the site AND to one of its records is two subjects and stays legal;
    /// unlinking frees the subject again.
    /// </summary>
    [Fact]
    public async Task NS2_LinkingADocumentTwiceToOneSubject_IsDocumentAlreadyLinked()
    {
        Guid siteId = await CreateSiteAsync();
        (Guid moduleId, Guid _) = await CreateFormAsync();
        SiteTestData.Owners owners = await SiteTestData.OwnersAsync(fixture);
        Guid documentId = await SiteTestData.CreateDocumentAsync(fixture);

        Result<Guid> record = await SendAsync(new AddSiteRecordCommand(
            siteId, new CreateSiteRecordRequest(moduleId, "Door"), SiteTestData.OwnerUserId), true);

        Result<Guid> toSite = await SendAsync(new LinkSiteDocumentCommand(
            siteId, new LinkSiteDocumentRequest(documentId, SiteDocumentKind.Photo),
            owners.EmployeeId, SiteTestData.OwnerUserId), true);
        Result<Guid> toRecord = await SendAsync(new LinkSiteDocumentCommand(
            siteId, new LinkSiteDocumentRequest(documentId, SiteDocumentKind.Photo, record.Value),
            owners.EmployeeId, SiteTestData.OwnerUserId), true);

        Assert.True(toSite.IsSuccess);
        Assert.True(toRecord.IsSuccess);

        Result<Guid> siteAgain = await SendAsync(new LinkSiteDocumentCommand(
            siteId, new LinkSiteDocumentRequest(documentId, SiteDocumentKind.Sketch),
            owners.EmployeeId, SiteTestData.OwnerUserId), true);
        Result<Guid> recordAgain = await SendAsync(new LinkSiteDocumentCommand(
            siteId, new LinkSiteDocumentRequest(documentId, SiteDocumentKind.Photo, record.Value),
            owners.EmployeeId, SiteTestData.OwnerUserId), true);

        Assert.Equal(SiteErrors.DocumentAlreadyLinked, siteAgain.Error);
        Assert.Equal(409, siteAgain.Error.Status);
        Assert.Equal(SiteErrors.DocumentAlreadyLinked, recordAgain.Error);

        Assert.True((await SendAsync(new UnlinkSiteDocumentCommand(
            siteId, toSite.Value, SiteTestData.OwnerUserId))).IsSuccess);

        Result<Guid> relinked = await SendAsync(new LinkSiteDocumentCommand(
            siteId, new LinkSiteDocumentRequest(documentId, SiteDocumentKind.Photo),
            owners.EmployeeId, SiteTestData.OwnerUserId), true);
        Assert.True(relinked.IsSuccess);

        Result<IReadOnlyList<SiteDocumentResponse>> listed = await ListDocumentsAsync(siteId);
        Assert.Equal(2, listed.Value!.Count);
    }

    private async Task<Guid> CreateCatalogItemAsync()
    {
        string suffix = Guid.NewGuid().ToString("N")[..8];
        Guid categoryId = (await SendAsync(new Crm.Contracts.Catalog.CreateCrmCatalogCategoryCommand(
            null, $"cat{suffix}", "Doors", "أبواب"), true)).Value;

        Result<Guid> item = await SendAsync(new Crm.Contracts.Catalog.CreateCrmCatalogItemCommand(
            categoryId, $"itm{suffix}", "Flush door", "باب"), true);
        Assert.True(item.IsSuccess);
        return item.Value;
    }

    /// <summary>
    /// <b>S2.</b> <c>CatalogItemId</c> was never validated: a missing id reached the foreign key as a
    /// 500, and a retired catalog item was accepted. Checked through <c>ICrmCatalogItemReader</c> on
    /// add, update and the grid save.
    /// </summary>
    [Fact]
    public async Task S2_RecordWithAMissingOrRetiredCatalogItem_IsCatalogItemNotFound()
    {
        Guid siteId = await CreateSiteAsync();
        (Guid moduleId, Guid itemId) = await CreateFormAsync();
        Guid catalogItemId = await CreateCatalogItemAsync();

        Result<Guid> valid = await SendAsync(new AddSiteRecordCommand(
            siteId, new CreateSiteRecordRequest(moduleId, "Door 1", CatalogItemId: catalogItemId),
            SiteTestData.OwnerUserId), true);
        Assert.True(valid.IsSuccess);

        Result<Guid> missingAdd = await SendAsync(new AddSiteRecordCommand(
            siteId, new CreateSiteRecordRequest(moduleId, "Door 2", CatalogItemId: Guid.NewGuid()),
            SiteTestData.OwnerUserId), true);
        Assert.Equal(SiteErrors.CatalogItemNotFound, missingAdd.Error);

        Result missingUpdate = await SendAsync(new UpdateSiteRecordCommand(
            siteId, valid.Value, new UpdateSiteRecordRequest("Door 1", null, Guid.NewGuid(), 0, null),
            SiteTestData.OwnerUserId));
        Assert.Equal(SiteErrors.CatalogItemNotFound, missingUpdate.Error);

        Result missingBulk = await SendAsync(new BulkSaveSiteRecordsCommand(
            siteId,
            new BulkSaveSiteRecordsRequest(
            [
                new BulkSiteRecordRequest(null, moduleId, "Door 3", null, Guid.NewGuid(), 0, null,
                    [new SiteValueRequest(itemId, ValueNumber: 90m)]),
            ]),
            SiteTestData.OwnerUserId));
        Assert.Equal(SiteErrors.CatalogItemNotFound, missingBulk.Error);

        await using (var connection = new NpgsqlConnection(fixture.ConnectionString))
        {
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand(
                $"""UPDATE crm.crm_catalog_items SET "IsActive" = false WHERE "Id" = '{catalogItemId}';""",
                connection);
            await command.ExecuteNonQueryAsync();
        }

        Result<Guid> retired = await SendAsync(new AddSiteRecordCommand(
            siteId, new CreateSiteRecordRequest(moduleId, "Door 4", CatalogItemId: catalogItemId),
            SiteTestData.OwnerUserId), true);
        Assert.Equal(SiteErrors.CatalogItemNotFound, retired.Error);
    }

    /// <summary>
    /// <b>B-S-H2.</b> A value save deleted EVERY stored value of the record before re-inserting the
    /// incoming set — including values for items detached from the module, which the caller cannot
    /// send (the validator refuses them). So detach, re-save, reattach silently lost the measurement the
    /// pivot test above promises survives a detach.
    /// </summary>
    [Fact]
    public async Task BSH2_ValueOfADetachedItem_SurvivesAResaveAndReturnsOnReattach()
    {
        Guid siteId = await CreateSiteAsync();
        (Guid moduleId, Guid widthId) = await CreateFormAsync();

        Guid heightId = (await SendAsync(new CreateSiteItemCommand(
            new CreateSiteItemRequest(Unique("height"), "Height", "الارتفاع", SiteItemDataType.Number, "cm")),
            true)).Value;
        Assert.True((await SendAsync(new AttachSiteItemCommand(
            moduleId, new AttachSiteItemRequest(heightId)), true)).IsSuccess);

        Result<Guid> record = await SendAsync(new AddSiteRecordCommand(
            siteId, new CreateSiteRecordRequest(moduleId, "Front door"), SiteTestData.OwnerUserId), true);

        Assert.True((await SendAsync(new SetSiteRecordValuesCommand(
            siteId, record.Value,
            new SetSiteRecordValuesRequest(
                [new SiteValueRequest(widthId, ValueNumber: 90m), new SiteValueRequest(heightId, ValueNumber: 210m)]),
            SiteTestData.OwnerUserId))).IsSuccess);

        Assert.True((await SendAsync(new DetachSiteItemCommand(moduleId, heightId))).IsSuccess);

        Assert.True((await SendAsync(new SetSiteRecordValuesCommand(
            siteId, record.Value,
            new SetSiteRecordValuesRequest([new SiteValueRequest(widthId, ValueNumber: 95m)]),
            SiteTestData.OwnerUserId))).IsSuccess);

        Assert.True((await SendAsync(new AttachSiteItemCommand(
            moduleId, new AttachSiteItemRequest(heightId)), true)).IsSuccess);

        Assert.Equal(95m, await ReadNumberAsync(record.Value, widthId));
        Assert.Equal(210m, await ReadNumberAsync(record.Value, heightId));
    }

    /// <summary>
    /// <b>S3 / NS1 / NS3.</b> Record, value and document writes never touched the site row, so its
    /// <c>xmin</c> was never checked: a grid save that loaded the survey as Draft could commit AFTER a
    /// submit had frozen it, and the approver would decide on content that changed under them.
    /// Deterministic interleave: scope A loads the site, scope B submits and commits, then A's grid
    /// save runs against its stale copy — exactly one of the two must lose with a 409.
    /// </summary>
    [Fact]
    public async Task S3_BulkSaveRacingASubmit_IsAConcurrencyConflict()
    {
        Guid siteId = await CreateSiteAsync();
        (Guid moduleId, Guid itemId) = await CreateFormAsync();

        await using AsyncServiceScope racer = fixture.CreateScope();
        racer.ServiceProvider.GetRequiredService<TestCurrentUser>().Grant(ProjectsPermissions.Projects.ReadAll);

        // A: load the site as Draft (tracked), the way the grid save's own load would.
        SitesDbContext db = racer.ServiceProvider.GetRequiredService<SitesDbContext>();
        Site stale = await db.Sites.SingleAsync(s => s.Id == siteId);
        Assert.Equal(Sites.Domain.SiteStatus.Draft, stale.Status);

        // B: submit and commit.
        Result submitted = await SendAsync(new SubmitSiteCommand(siteId, SiteTestData.OwnerEmployeeId, SiteTestData.OwnerUserId));
        Assert.True(submitted.IsSuccess);

        // A: the grid save on its stale copy.
        Result saved = await racer.ServiceProvider
            .GetRequiredService<ICommandHandler<BulkSaveSiteRecordsCommand>>()
            .HandleAsync(new BulkSaveSiteRecordsCommand(
                siteId,
                new BulkSaveSiteRecordsRequest(
                [
                    new BulkSiteRecordRequest(null, moduleId, "Late door", null, null, 0, null,
                        [new SiteValueRequest(itemId, ValueNumber: 90m)]),
                ]),
                SiteTestData.OwnerUserId));

        Assert.Equal(SiteErrors.ConcurrencyConflict, saved.Error);
        Assert.Equal(409, saved.Error.Status);

        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            $"""SELECT count(*) FROM sites.site_records WHERE "SiteId"='{siteId}';""", connection);
        Assert.Equal(0, Convert.ToInt32(await command.ExecuteScalarAsync(), CultureInfo.InvariantCulture));
    }

    /// <summary>
    /// <b>NS1.</b> Two value saves on one record from two tablets: the second, staged against a stale
    /// read, used to overwrite (or collide on the composite key) instead of being told to reload. The
    /// record's <c>xmin</c> now guards its value set.
    /// </summary>
    [Fact]
    public async Task NS1_ValueSaveOnAStaleRecord_IsAConcurrencyConflict()
    {
        Guid siteId = await CreateSiteAsync();
        (Guid moduleId, Guid itemId) = await CreateFormAsync();

        Result<Guid> record = await SendAsync(new AddSiteRecordCommand(
            siteId, new CreateSiteRecordRequest(moduleId, "Front door"), SiteTestData.OwnerUserId), true);

        await using AsyncServiceScope racer = fixture.CreateScope();
        racer.ServiceProvider.GetRequiredService<TestCurrentUser>().Grant(ProjectsPermissions.Projects.ReadAll);
        SitesDbContext db = racer.ServiceProvider.GetRequiredService<SitesDbContext>();
        _ = await db.SiteRecords.SingleAsync(r => r.Id == record.Value);

        Assert.True((await SendAsync(new SetSiteRecordValuesCommand(
            siteId, record.Value,
            new SetSiteRecordValuesRequest([new SiteValueRequest(itemId, ValueNumber: 90m)]),
            SiteTestData.OwnerUserId))).IsSuccess);

        Result late = await racer.ServiceProvider
            .GetRequiredService<ICommandHandler<SetSiteRecordValuesCommand>>()
            .HandleAsync(new SetSiteRecordValuesCommand(
                siteId, record.Value,
                new SetSiteRecordValuesRequest([new SiteValueRequest(itemId, ValueNumber: 120m)]),
                SiteTestData.OwnerUserId));

        Assert.Equal(SiteErrors.ConcurrencyConflict, late.Error);
        Assert.Equal(90m, await ReadNumberAsync(record.Value, itemId));
    }

    /// <summary><b>NS3.</b> A document link staged against a Draft read must not land on a survey
    /// submitted in between.</summary>
    [Fact]
    public async Task NS3_DocumentLinkRacingASubmit_IsAConcurrencyConflict()
    {
        Guid siteId = await CreateSiteAsync();
        SiteTestData.Owners owners = await SiteTestData.OwnersAsync(fixture);
        Guid documentId = await SiteTestData.CreateDocumentAsync(fixture);

        await using AsyncServiceScope racer = fixture.CreateScope();
        racer.ServiceProvider.GetRequiredService<TestCurrentUser>().Grant(ProjectsPermissions.Projects.ReadAll);
        SitesDbContext db = racer.ServiceProvider.GetRequiredService<SitesDbContext>();
        _ = await db.Sites.SingleAsync(s => s.Id == siteId);

        Assert.True((await SendAsync(new SubmitSiteCommand(siteId, SiteTestData.OwnerEmployeeId, SiteTestData.OwnerUserId))).IsSuccess);

        Result<Guid> late = await racer.ServiceProvider
            .GetRequiredService<ICommandHandler<LinkSiteDocumentCommand, Guid>>()
            .HandleAsync(new LinkSiteDocumentCommand(
                siteId, new LinkSiteDocumentRequest(documentId, SiteDocumentKind.Photo),
                owners.EmployeeId, SiteTestData.OwnerUserId));

        Assert.Equal(SiteErrors.ConcurrencyConflict, late.Error);
    }

    /// <summary>
    /// <b>S4.</b> Two grid rows naming one RecordId were both applied to the same tracked record — the
    /// second row's values staged on top of the first's — instead of being refused as a malformed grid.
    /// </summary>
    [Fact]
    public async Task S4_BulkSaveNamingOneRecordTwice_IsAValidationError()
    {
        Guid siteId = await CreateSiteAsync();
        (Guid moduleId, Guid itemId) = await CreateFormAsync();

        Result<Guid> record = await SendAsync(new AddSiteRecordCommand(
            siteId, new CreateSiteRecordRequest(moduleId, "Door 1"), SiteTestData.OwnerUserId), true);

        Result result = await SendAsync(new BulkSaveSiteRecordsCommand(
            siteId,
            new BulkSaveSiteRecordsRequest(
            [
                new BulkSiteRecordRequest(record.Value, moduleId, "Door A", null, null, 0, null,
                    [new SiteValueRequest(itemId, ValueNumber: 90m)]),
                new BulkSiteRecordRequest(record.Value, moduleId, "Door B", null, null, 1, null,
                    [new SiteValueRequest(itemId, ValueNumber: 95m)]),
            ]),
            SiteTestData.OwnerUserId));

        ValidationError validation = Assert.IsType<ValidationError>(result.Error);
        Assert.Contains(validation.Errors, e => e.Description.Contains("distinct", StringComparison.OrdinalIgnoreCase));
        Assert.Equal("Door 1", await ReadLabelAsync(record.Value));
    }

    /// <summary>
    /// <b>S5.</b> Out-of-range input reached the domain or the database instead of a 400: unknown enum
    /// numbers were cast straight through, an area past <c>numeric(8,2)</c> or a number past
    /// <c>numeric(18,4)</c> overflowed at commit (500), and text past the 1024-wide value column did
    /// the same. Each is now a <see cref="ValidationError"/> naming the field.
    /// </summary>
    [Fact]
    public async Task S5_OutOfRangeInput_IsAValidationErrorEverywhere()
    {
        SiteTestData.Owners owners = await SiteTestData.OwnersAsync(fixture);

        Result<Guid> badEnum = await SendAsync(new CreateSiteCommand(
            new CreateSiteRequest(owners.ProjectId, new SiteUnitDataRequest(UnitType: (Sites.Contracts.UnitType)99)),
            owners.EmployeeId, SiteTestData.OwnerUserId), true);
        Assert.IsType<ValidationError>(badEnum.Error);

        Result<Guid> hugeArea = await SendAsync(new CreateSiteCommand(
            new CreateSiteRequest(owners.ProjectId, new SiteUnitDataRequest(AreaSqm: 1_000_000m)),
            owners.EmployeeId, SiteTestData.OwnerUserId), true);
        Assert.IsType<ValidationError>(hugeArea.Error);

        Result<Guid> badType = await SendAsync(new CreateSiteItemCommand(
            new CreateSiteItemRequest(Unique("bad"), "Bad", "سيء", (SiteItemDataType)99)), true);
        Assert.IsType<ValidationError>(badType.Error);

        Result<Guid> hugeBound = await SendAsync(new CreateSiteItemCommand(
            new CreateSiteItemRequest(Unique("big"), "Big", "كبير", SiteItemDataType.Number,
                MaxValue: 1e15m)), true);
        Assert.IsType<ValidationError>(hugeBound.Error);

        Guid siteId = await CreateSiteAsync();
        Guid moduleId = (await SendAsync(new CreateSiteModuleCommand(
            new CreateSiteModuleRequest(Unique("free"), "Free", "حر")), true)).Value;
        Guid numberId = (await SendAsync(new CreateSiteItemCommand(
            new CreateSiteItemRequest(Unique("n"), "N", "ن", SiteItemDataType.Number)), true)).Value;
        Guid textId = (await SendAsync(new CreateSiteItemCommand(
            new CreateSiteItemRequest(Unique("t"), "T", "ت", SiteItemDataType.Text)), true)).Value;
        await SendAsync(new AttachSiteItemCommand(moduleId, new AttachSiteItemRequest(numberId)), true);
        await SendAsync(new AttachSiteItemCommand(moduleId, new AttachSiteItemRequest(textId)), true);

        Result<Guid> record = await SendAsync(new AddSiteRecordCommand(
            siteId, new CreateSiteRecordRequest(moduleId, "Wall"), SiteTestData.OwnerUserId), true);

        Result hugeNumber = await SendAsync(new SetSiteRecordValuesCommand(
            siteId, record.Value,
            new SetSiteRecordValuesRequest([new SiteValueRequest(numberId, ValueNumber: 1e15m)]),
            SiteTestData.OwnerUserId));
        Assert.IsType<ValidationError>(hugeNumber.Error);

        Result longText = await SendAsync(new SetSiteRecordValuesCommand(
            siteId, record.Value,
            new SetSiteRecordValuesRequest([new SiteValueRequest(textId, ValueText: new string('x', 1025))]),
            SiteTestData.OwnerUserId));
        Assert.IsType<ValidationError>(longText.Error);

        Result<Guid> badKind = await SendAsync(new LinkSiteDocumentCommand(
            siteId, new LinkSiteDocumentRequest(await SiteTestData.CreateDocumentAsync(fixture), (SiteDocumentKind)99),
            owners.EmployeeId, SiteTestData.OwnerUserId), true);
        Assert.IsType<ValidationError>(badKind.Error);
    }

    /// <summary>
    /// <b>S9.</b> Swapping two labels in one grid save was refused: each row's stored-label check
    /// excluded only its own record, so it saw the other row's OLD label as taken — and had it passed,
    /// the single UPDATE batch would have hit <c>ux_site_records_label</c> mid-statement as a 500. Now the
    /// stored check excludes every record in the batch and renames go through temporary labels in one
    /// transaction. Freeing a label and reusing it for a new row in the same save works the same way.
    /// </summary>
    [Fact]
    public async Task S9_SwappingTwoLabelsInOneBulkSave_Succeeds()
    {
        Guid siteId = await CreateSiteAsync();
        (Guid moduleId, Guid itemId) = await CreateFormAsync();

        Result<Guid> first = await SendAsync(new AddSiteRecordCommand(
            siteId, new CreateSiteRecordRequest(moduleId, "Door A"), SiteTestData.OwnerUserId), true);
        Result<Guid> second = await SendAsync(new AddSiteRecordCommand(
            siteId, new CreateSiteRecordRequest(moduleId, "Door B"), SiteTestData.OwnerUserId), true);

        Result swapped = await SendAsync(new BulkSaveSiteRecordsCommand(
            siteId,
            new BulkSaveSiteRecordsRequest(
            [
                new BulkSiteRecordRequest(first.Value, moduleId, "Door B", null, null, 0, null,
                    [new SiteValueRequest(itemId, ValueNumber: 90m)]),
                new BulkSiteRecordRequest(second.Value, moduleId, "door a", null, null, 1, null,
                    [new SiteValueRequest(itemId, ValueNumber: 95m)]),
            ]),
            SiteTestData.OwnerUserId));

        Assert.True(swapped.IsSuccess, swapped.Error.Code);
        Assert.Equal("Door B", await ReadLabelAsync(first.Value));
        Assert.Equal("door a", await ReadLabelAsync(second.Value));

        Result reused = await SendAsync(new BulkSaveSiteRecordsCommand(
            siteId,
            new BulkSaveSiteRecordsRequest(
            [
                new BulkSiteRecordRequest(first.Value, moduleId, "Door C", null, null, 0, null,
                    [new SiteValueRequest(itemId, ValueNumber: 90m)]),
                new BulkSiteRecordRequest(null, moduleId, "Door B", null, null, 2, null,
                    [new SiteValueRequest(itemId, ValueNumber: 80m)]),
            ]),
            SiteTestData.OwnerUserId));

        Assert.True(reused.IsSuccess, reused.Error.Code);
        Assert.Equal("Door C", await ReadLabelAsync(first.Value));
    }

    /// <summary><b>S9.</b> The temporary swap labels are reserved: a caller cannot name one.</summary>
    [Fact]
    public async Task S9_ALabelWithTheReservedPrefix_IsAValidationError()
    {
        Guid siteId = await CreateSiteAsync();
        (Guid moduleId, Guid _) = await CreateFormAsync();

        Result<Guid> result = await SendAsync(new AddSiteRecordCommand(
            siteId, new CreateSiteRecordRequest(moduleId, "__swap_x"), SiteTestData.OwnerUserId), true);

        Assert.IsType<ValidationError>(result.Error);
    }

    /// <summary>
    /// <b>S12.</b> A deactivated module still took new records: the single add only checked the module
    /// EXISTS, and the grid save did not check insert rows' module at all — only its fields, which a
    /// retired module keeps.
    /// </summary>
    [Fact]
    public async Task S12_NewRecordsInADeactivatedModule_AreModuleNotFound()
    {
        Guid siteId = await CreateSiteAsync();
        (Guid moduleId, Guid itemId) = await CreateFormAsync();

        Assert.True((await SendAsync(new SetSiteModuleActiveCommand(moduleId, IsActive: false))).IsSuccess);

        Result<Guid> single = await SendAsync(new AddSiteRecordCommand(
            siteId, new CreateSiteRecordRequest(moduleId, "Door"), SiteTestData.OwnerUserId), true);
        Assert.Equal(SiteErrors.ModuleNotFound, single.Error);

        Result bulk = await SendAsync(new BulkSaveSiteRecordsCommand(
            siteId,
            new BulkSaveSiteRecordsRequest(
            [
                new BulkSiteRecordRequest(null, moduleId, "Door", null, null, 0, null,
                    [new SiteValueRequest(itemId, ValueNumber: 90m)]),
            ]),
            SiteTestData.OwnerUserId));
        Assert.Equal(SiteErrors.ModuleNotFound, bulk.Error);
    }

    /// <summary>
    /// <b>B-S-L5.</b> A module with no active fields — new, or every field detached — answered
    /// <c>ModuleNotFound</c> to an empty value set, so its records could be neither saved from the grid
    /// nor have their (empty) values set.
    /// </summary>
    [Fact]
    public async Task BSL5_AModuleWithNoActiveFields_AcceptsAnEmptyValueSet()
    {
        Guid siteId = await CreateSiteAsync();
        Guid moduleId = (await SendAsync(new CreateSiteModuleCommand(
            new CreateSiteModuleRequest(Unique("bare"), "Bare", "فارغ")), true)).Value;

        Result<Guid> record = await SendAsync(new AddSiteRecordCommand(
            siteId, new CreateSiteRecordRequest(moduleId, "Room"), SiteTestData.OwnerUserId), true);
        Assert.True(record.IsSuccess);

        Result single = await SendAsync(new SetSiteRecordValuesCommand(
            siteId, record.Value, new SetSiteRecordValuesRequest([]), SiteTestData.OwnerUserId));
        Assert.True(single.IsSuccess, single.Error.Code);

        Result bulk = await SendAsync(new BulkSaveSiteRecordsCommand(
            siteId,
            new BulkSaveSiteRecordsRequest(
            [
                new BulkSiteRecordRequest(record.Value, moduleId, "Room", null, null, 0, null, []),
                new BulkSiteRecordRequest(null, moduleId, "Hall", null, null, 1, null, []),
            ]),
            SiteTestData.OwnerUserId));
        Assert.True(bulk.IsSuccess, bulk.Error.Code);

        // A value for a field the module does not have is still refused.
        Result unknown = await SendAsync(new SetSiteRecordValuesCommand(
            siteId, record.Value,
            new SetSiteRecordValuesRequest([new SiteValueRequest(Guid.NewGuid(), ValueNumber: 1m)]),
            SiteTestData.OwnerUserId));
        Assert.IsType<ValidationError>(unknown.Error);
    }

    // ── Pivot ──────────────────────────────────────────────────────────────────

    /// <summary>
    /// <b>Defect B9, first half.</b> The backend builds every pivot column with an EMPTY option list, so
    /// a client rendering a Select column from the pivot alone has no choices to offer.
    /// </summary>
    [Fact]
    public async Task Pivot_ColumnsCarryTheirOptions()
    {
        Guid siteId = await CreateSiteAsync();

        Guid moduleId = (await SendAsync(new CreateSiteModuleCommand(
            new CreateSiteModuleRequest(Unique("sel"), "Openings", "الفتحات")), true)).Value;

        Guid itemId = (await SendAsync(new CreateSiteItemCommand(
            new CreateSiteItemRequest(Unique("leaf"), "Leaf", "درفة", SiteItemDataType.Select)), true)).Value;

        Assert.True((await SendAsync(new AddSiteItemOptionCommand(
            itemId, new SiteItemOptionRequest("single", "Single", "مفردة")), true)).IsSuccess);

        await SendAsync(new AttachSiteItemCommand(moduleId, new AttachSiteItemRequest(itemId)), true);

        Result<SitePivotResponse> pivot = await ResolveAsync(sp => sp
            .GetRequiredService<IQueryHandler<GetSitePivotQuery, SitePivotResponse>>()
            .HandleAsync(new GetSitePivotQuery(siteId, moduleId, SiteTestData.OwnerUserId)));

        Assert.True(pivot.IsSuccess);
        SiteSchemaFieldResponse column = Assert.Single(pivot.Value!.Columns);
        Assert.Single(column.Options);
    }

    /// <summary>
    /// <b>Defect B10.</b> The backend omits the module's <c>IsActive</c> filter here while the schema
    /// route requires it, so the pivot rendered a retired module's grid that the schema route 404s for.
    /// </summary>
    [Fact]
    public async Task Pivot_ForADeactivatedModule_IsNotFound()
    {
        Guid siteId = await CreateSiteAsync();
        (Guid moduleId, Guid _) = await CreateFormAsync();

        Assert.True((await SendAsync(new SetSiteModuleActiveCommand(moduleId, IsActive: false))).IsSuccess);

        Result<SitePivotResponse> pivot = await ResolveAsync(sp => sp
            .GetRequiredService<IQueryHandler<GetSitePivotQuery, SitePivotResponse>>()
            .HandleAsync(new GetSitePivotQuery(siteId, moduleId, SiteTestData.OwnerUserId)));

        Assert.Equal(SiteErrors.ModuleNotFound, pivot.Error);
    }

    /// <summary>
    /// <b>Defect B9, second half.</b> The backend assigns cells through a <c>Dictionary</c> indexer,
    /// which ADDS missing keys — so a value captured against an item since detached appeared as an extra
    /// cell with no matching column: the ragged row the code one line above promises never to produce.
    /// </summary>
    [Fact]
    public async Task Pivot_AfterAnItemIsDetached_HasNoCellWithoutAColumn()
    {
        Guid siteId = await CreateSiteAsync();
        (Guid moduleId, Guid itemId) = await CreateFormAsync();

        Result<Guid> record = await SendAsync(new AddSiteRecordCommand(
            siteId, new CreateSiteRecordRequest(moduleId, "Front door"), SiteTestData.OwnerUserId), true);

        await SendAsync(new SetSiteRecordValuesCommand(
            siteId, record.Value,
            new SetSiteRecordValuesRequest([new SiteValueRequest(itemId, ValueNumber: 90m)]),
            SiteTestData.OwnerUserId));

        // PC-03 discrimination, as a before/after pair. Detaching the only item leaves a pivot with no
        // columns and no cells, so the ragged-row assertion below is satisfied by an empty row — which
        // is equally what a projection emitting no cells at all would give. Reading the pivot WHILE the
        // item is attached proves it really does emit one, so the emptiness afterwards is the detach's
        // doing rather than the query's.
        SitePivotResponse attached = await ReadPivotAsync(siteId, moduleId);
        Assert.Single(attached.Columns);
        Assert.Single(Assert.Single(attached.Rows).Cells);

        // The value survives the detach by design -- reattaching makes it visible again.
        Assert.True((await SendAsync(new DetachSiteItemCommand(moduleId, itemId))).IsSuccess);

        SitePivotResponse pivot = await ReadPivotAsync(siteId, moduleId);

        var columnCodes = pivot.Columns.Select(c => c.Code).ToHashSet();
        SitePivotRowResponse row = Assert.Single(pivot.Rows);

        Assert.Empty(row.Cells.Keys.Except(columnCodes));
    }

    /// <summary>The pivot read both halves of the detach assertion go through, so the control cannot
    /// drift from the thing it controls.</summary>
    private async Task<SitePivotResponse> ReadPivotAsync(Guid siteId, Guid moduleId)
    {
        Result<SitePivotResponse> pivot = await ResolveAsync(sp => sp
            .GetRequiredService<IQueryHandler<GetSitePivotQuery, SitePivotResponse>>()
            .HandleAsync(new GetSitePivotQuery(siteId, moduleId, SiteTestData.OwnerUserId)));

        Assert.True(pivot.IsSuccess);
        return pivot.Value!;
    }

    // ── Families ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task Families_GroupsRecordsThatMeasureTheSameThing()
    {
        Guid siteId = await CreateSiteAsync();
        (Guid moduleId, Guid itemId) = await CreateFormAsync();

        foreach ((string? label, decimal width) in new[] { ("Door 1", 90m), ("Door 2", 90m), ("Door 3", 100m) })
        {
            Result<Guid> record = await SendAsync(new AddSiteRecordCommand(
                siteId, new CreateSiteRecordRequest(moduleId, label), SiteTestData.OwnerUserId), true);

            await SendAsync(new SetSiteRecordValuesCommand(
                siteId, record.Value,
                new SetSiteRecordValuesRequest([new SiteValueRequest(itemId, ValueNumber: width)]),
                SiteTestData.OwnerUserId));
        }

        Result<SiteFamiliesResponse> families = await ResolveAsync(sp => sp
            .GetRequiredService<IQueryHandler<GetSiteFamiliesQuery, SiteFamiliesResponse>>()
            .HandleAsync(new GetSiteFamiliesQuery(siteId, moduleId, null, SiteTestData.OwnerUserId)));

        Assert.True(families.IsSuccess);

        // Two 90s collapse into one family of 2; the 100 is its own.
        Assert.Equal(2, families.Value!.Families.Count);
        Assert.Contains(families.Value!.Families, f => f.Instances == 2);
        Assert.Contains(families.Value!.Families, f => f.Instances == 1);
    }

    [Fact]
    public async Task Families_WithAnUnknownGroupByToken_IsABadRequestNamingIt()
    {
        Guid siteId = await CreateSiteAsync();
        (Guid moduleId, Guid _) = await CreateFormAsync();

        Result<SiteFamiliesResponse> families = await ResolveAsync(sp => sp
            .GetRequiredService<IQueryHandler<GetSiteFamiliesQuery, SiteFamiliesResponse>>()
            .HandleAsync(new GetSiteFamiliesQuery(
                siteId, moduleId, "not_a_field", SiteTestData.OwnerUserId)));

        Assert.True(families.IsFailure);
        Assert.Equal("sites.families.UnknownGroupBy", families.Error.Code);
        Assert.Contains("not_a_field", families.Error.Description, StringComparison.Ordinal);
    }

    /// <summary>
    /// <b>Defect B14.</b> The backend appends one entry per requested token with no distinct check, so
    /// <c>?groupBy=width,width</c> produced a doubled family key — and <c>FamilyKey</c> is documented as
    /// the exporter's join token, so a caller could mint keys that match nothing.
    /// </summary>
    [Fact]
    public async Task Families_WithARepeatedGroupByToken_DeduplicatesTheKey()
    {
        Guid siteId = await CreateSiteAsync();
        (Guid moduleId, Guid itemId) = await CreateFormAsync();

        string code = await SiteTestData.ItemCodeAsync(fixture, itemId);

        Result<SiteFamiliesResponse> families = await ResolveAsync(sp => sp
            .GetRequiredService<IQueryHandler<GetSiteFamiliesQuery, SiteFamiliesResponse>>()
            .HandleAsync(new GetSiteFamiliesQuery(
                siteId, moduleId, $"{code},{code}", SiteTestData.OwnerUserId)));

        Assert.True(families.IsSuccess);
        Assert.Single(families.Value!.GroupBy);
    }
}
