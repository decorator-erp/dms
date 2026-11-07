using System.Globalization;
using BuildingBlocks.Messaging;
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

        await SendAsync(new SubmitSiteCommand(siteId, SiteTestData.OwnerUserId));
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

        Assert.True((await SendAsync(new SubmitSiteCommand(siteId, SiteTestData.OwnerUserId))).IsSuccess);

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

        Assert.True((await SendAsync(new SubmitSiteCommand(siteId, SiteTestData.OwnerUserId))).IsSuccess);

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
