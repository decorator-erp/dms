using BuildingBlocks.Messaging;
using BuildingBlocks.Outbox;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Projects.Contracts;
using SharedKernel;
using Sites.Contracts;
using Sites.Contracts.Surveys;
using Sites.Domain;
// The eight enum pairs are duplicated between Contracts and Domain by construction (see
// SiteEnumMapping); these tests speak Contracts, while SiteErrors comes from Domain.
using SiteStatus = Sites.Contracts.SiteStatus;
using Xunit;

namespace Sites.Application.Tests;

/// <summary>
/// The instance layer end to end, through the real DI graph against a real Postgres.
///
/// <para><b>Every write assertion reads the row back.</b> The plan names the hazard: the backend's
/// repository defaulted to <c>asNoTracking: true</c> with all sixteen mutating call sites overriding it,
/// and EF here tracks by default — so an <c>AsNoTracking()</c> added to a write path makes the write
/// vanish with no error, and a test that only inspects the returned <c>Result</c> still passes. These
/// assert what the database holds afterwards.</para>
/// </summary>
[Collection(SitesApplicationCollection.Name)]
public sealed class SiteLifecycleFlowTests(SitesApplicationFixture fixture)
{
    private async Task<Guid> CreateSiteAsync(string? address = "12 Nasr Road")
    {
        var owners = await SiteTestData.OwnersAsync(fixture);

        await using var scope = fixture.CreateScope();
        scope.ServiceProvider.GetRequiredService<TestCurrentUser>()
            .Grant(ProjectsPermissions.Projects.ReadAll);

        var result = await scope.ServiceProvider
            .GetRequiredService<ICommandHandler<CreateSiteCommand, Guid>>()
            .HandleAsync(new CreateSiteCommand(
                new CreateSiteRequest(owners.ProjectId, new SiteUnitDataRequest(Address: address)),
                owners.EmployeeId, SiteTestData.OwnerUserId));

        Assert.True(result.IsSuccess);
        return result.Value;
    }

    private async Task<Result> SendAsync<TCommand>(TCommand command)
        where TCommand : ICommand
    {
        await using var scope = fixture.CreateScope();
        scope.ServiceProvider.GetRequiredService<TestCurrentUser>()
            .Grant(ProjectsPermissions.Projects.ReadAll);

        return await scope.ServiceProvider
            .GetRequiredService<ICommandHandler<TCommand>>()
            .HandleAsync(command);
    }

    private async Task<(int Status, string? Address, DateTime? SubmittedAt, bool IsActive)> ReadRowAsync(
        Guid siteId)
    {
        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();

        await using var command = new NpgsqlCommand(
            $"""SELECT "Status","Address","SubmittedAtUtc","IsActive" FROM sites.sites WHERE "Id"='{siteId}';""",
            connection);

        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());

        return (
            reader.GetInt32(0),
            await reader.IsDBNullAsync(1) ? null : reader.GetString(1),
            await reader.IsDBNullAsync(2) ? null : reader.GetDateTime(2),
            reader.GetBoolean(3));
    }

    // ── Creation ───────────────────────────────────────────────────────────────

    /// <summary>The code comes from the sequence, in the format the code check constraint demands.</summary>
    [Fact]
    public async Task CreateSite_DrawsItsCodeFromTheSequence()
    {
        var first = await CreateSiteAsync();
        var second = await CreateSiteAsync();

        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();

        await using var command = new NpgsqlCommand(
            $"""SELECT "Code" FROM sites.sites WHERE "Id" IN ('{first}','{second}') ORDER BY "Code";""",
            connection);

        await using var reader = await command.ExecuteReaderAsync();
        var codes = new List<string>();
        while (await reader.ReadAsync()) codes.Add(reader.GetString(0));

        Assert.Equal(2, codes.Count);
        Assert.All(codes, c => Assert.Matches("^SITE-[0-9]{5,}$", c));
        Assert.NotEqual(codes[0], codes[1]);
    }

    /// <summary>
    /// Defect B1's fix. The caller holds <c>Projects.ReadAll</c>, which short-circuits the access guard
    /// WITHOUT touching the projects table — so before this fix a garbage project id reached the foreign
    /// key and came back as an unexplained 500. <c>SiteErrors.ProjectNotFound</c> was dead code.
    /// </summary>
    [Fact]
    public async Task CreateSite_OnANonexistentProject_IsProjectNotFoundRatherThanAWriteFailure()
    {
        await using var scope = fixture.CreateScope();
        scope.ServiceProvider.GetRequiredService<TestCurrentUser>()
            .Grant(ProjectsPermissions.Projects.ReadAll);

        var result = await scope.ServiceProvider
            .GetRequiredService<ICommandHandler<CreateSiteCommand, Guid>>()
            .HandleAsync(new CreateSiteCommand(
                new CreateSiteRequest(Guid.NewGuid(), new SiteUnitDataRequest(Address: "Nowhere")),
                (await SiteTestData.OwnersAsync(fixture)).EmployeeId, SiteTestData.OwnerUserId));

        Assert.Equal(SiteErrors.ProjectNotFound, result.Error);
        Assert.Equal(404, result.Error.Status);
    }

    // ── The lifecycle, asserted against the stored row ─────────────────────────

    [Fact]
    public async Task Submit_PersistsTheStatusAndTheStamp()
    {
        var siteId = await CreateSiteAsync();

        Assert.True((await SendAsync(new SubmitSiteCommand(siteId, SiteTestData.OwnerUserId))).IsSuccess);

        var row = await ReadRowAsync(siteId);
        Assert.Equal(2, row.Status);
        Assert.NotNull(row.SubmittedAt);
    }

    [Fact]
    public async Task Submit_WithoutAnAddress_IsRefusedAndPersistsNothing()
    {
        var siteId = await CreateSiteAsync(address: null);

        var result = await SendAsync(new SubmitSiteCommand(siteId, SiteTestData.OwnerUserId));

        Assert.Equal(SiteErrors.AddressRequiredToSubmit, result.Error);

        var row = await ReadRowAsync(siteId);
        Assert.Equal(1, row.Status);
        Assert.Null(row.SubmittedAt);
    }

    [Fact]
    public async Task Approve_PersistsTheDecisionAndLocksTheSurvey()
    {
        var siteId = await CreateSiteAsync();
        var owners = await SiteTestData.OwnersAsync(fixture);

        Assert.True((await SendAsync(new SubmitSiteCommand(siteId, SiteTestData.OwnerUserId))).IsSuccess);
        Assert.True((await SendAsync(new DecideSiteCommand(
            siteId, new DecideSiteRequest(Approved: true, "Checked."),
            owners.EmployeeId, SiteTestData.OwnerUserId))).IsSuccess);

        Assert.Equal(3, (await ReadRowAsync(siteId)).Status);

        // And the lock is real: a header edit is now refused.
        var update = await SendAsync(new UpdateSiteCommand(
            siteId, new UpdateSiteRequest(new SiteUnitDataRequest(Address: "Somewhere else")),
            SiteTestData.OwnerUserId));

        Assert.Equal(SiteErrors.SiteLocked, update.Error);
        Assert.Equal("12 Nasr Road", (await ReadRowAsync(siteId)).Address);
    }

    /// <summary>
    /// <b>B12's fix, decided at P5F-02.</b> The lock now starts at <c>Submitted</c>, not at
    /// <c>Approved</c> — so the content an approver is shown is the content they decide about. The
    /// backend allowed this edit, invisibly.
    ///
    /// <para>The second half is the half that makes the freeze affordable rather than a dead end: reject
    /// → reopen → the same edit succeeds. A freeze with no correction path would just be a different
    /// defect.</para>
    /// </summary>
    [Fact]
    public async Task Submit_LocksTheSurvey_AndReopeningAfterARejectionUnlocksIt()
    {
        var siteId = await CreateSiteAsync();
        var owners = await SiteTestData.OwnersAsync(fixture);

        Assert.True((await SendAsync(new SubmitSiteCommand(siteId, SiteTestData.OwnerUserId))).IsSuccess);

        var blocked = await SendAsync(new UpdateSiteCommand(
            siteId, new UpdateSiteRequest(new SiteUnitDataRequest(Address: "Changed after review")),
            SiteTestData.OwnerUserId));

        Assert.Equal(SiteErrors.SiteLocked, blocked.Error);
        Assert.Equal(409, blocked.Error.Status);
        Assert.Equal("12 Nasr Road", (await ReadRowAsync(siteId)).Address);

        // The correction path, end to end: reject, reopen, and the same edit now lands.
        Assert.True((await SendAsync(new DecideSiteCommand(
            siteId, new DecideSiteRequest(Approved: false, "Remeasure the shaft."),
            owners.EmployeeId, SiteTestData.OwnerUserId))).IsSuccess);
        Assert.True((await SendAsync(new ReopenSiteCommand(siteId, SiteTestData.OwnerUserId))).IsSuccess);

        Assert.True((await SendAsync(new UpdateSiteCommand(
            siteId, new UpdateSiteRequest(new SiteUnitDataRequest(Address: "Changed after review")),
            SiteTestData.OwnerUserId))).IsSuccess);

        Assert.Equal("Changed after review", (await ReadRowAsync(siteId)).Address);
    }

    [Fact]
    public async Task RejectThenReopen_ReturnsToDraftAndClearsTheStampsInTheDatabase()
    {
        var siteId = await CreateSiteAsync();
        var owners = await SiteTestData.OwnersAsync(fixture);

        Assert.True((await SendAsync(new SubmitSiteCommand(siteId, SiteTestData.OwnerUserId))).IsSuccess);
        Assert.True((await SendAsync(new DecideSiteCommand(
            siteId, new DecideSiteRequest(Approved: false, "Rework the openings."),
            owners.EmployeeId, SiteTestData.OwnerUserId))).IsSuccess);

        Assert.Equal(4, (await ReadRowAsync(siteId)).Status);

        Assert.True((await SendAsync(new ReopenSiteCommand(siteId, SiteTestData.OwnerUserId))).IsSuccess);

        var row = await ReadRowAsync(siteId);
        Assert.Equal(1, row.Status);

        // All four stamps cleared -- the row would violate ck_sites_status_stamps otherwise, so this
        // passing at all is the constraint agreeing with the entity.
        Assert.Null(row.SubmittedAt);
    }

    [Fact]
    public async Task Update_PersistsTheChange()
    {
        var siteId = await CreateSiteAsync();

        Assert.True((await SendAsync(new UpdateSiteCommand(
            siteId, new UpdateSiteRequest(new SiteUnitDataRequest(Address: "9 Tahrir St")),
            SiteTestData.OwnerUserId))).IsSuccess);

        Assert.Equal("9 Tahrir St", (await ReadRowAsync(siteId)).Address);
    }

    /// <summary>
    /// Defect B13's fix. The backend soft-deletes at any status including Approved, while
    /// <c>Update</c> refuses — and since the query filter then hides the row from every read AND from the
    /// scope decorator's own resolution, an approved engineering record could be made to vanish with no
    /// lifecycle check.
    /// </summary>
    [Fact]
    public async Task Delete_OnAnApprovedSurvey_IsRefusedAndTheRowSurvives()
    {
        var siteId = await CreateSiteAsync();
        var owners = await SiteTestData.OwnersAsync(fixture);

        await SendAsync(new SubmitSiteCommand(siteId, SiteTestData.OwnerUserId));
        await SendAsync(new DecideSiteCommand(
            siteId, new DecideSiteRequest(Approved: true), owners.EmployeeId, SiteTestData.OwnerUserId));

        var result = await SendAsync(new DeleteSiteCommand(siteId, SiteTestData.OwnerUserId));

        Assert.Equal(SiteErrors.SiteLocked, result.Error);
        Assert.True((await ReadRowAsync(siteId)).IsActive);
    }

    [Fact]
    public async Task Delete_OnADraftSurvey_SoftDeletesIt()
    {
        var siteId = await CreateSiteAsync();

        Assert.True((await SendAsync(new DeleteSiteCommand(siteId, SiteTestData.OwnerUserId))).IsSuccess);

        Assert.False((await ReadRowAsync(siteId)).IsActive);
    }

    // ── Integration events ─────────────────────────────────────────────────────

    /// <summary>
    /// Submit and approve each enqueue an outbox row in the SAME <c>SaveChanges</c> as the status change,
    /// so the event and the write commit together or not at all.
    ///
    /// <para>No handler is registered for either event in this repo — a supported no-op, stated on the
    /// event types themselves rather than left to be discovered.</para>
    /// </summary>
    [Fact]
    public async Task SubmitAndApprove_EachEnqueueAnOutboxMessage()
    {
        var siteId = await CreateSiteAsync();
        var owners = await SiteTestData.OwnersAsync(fixture);

        await SendAsync(new SubmitSiteCommand(siteId, SiteTestData.OwnerUserId));
        await SendAsync(new DecideSiteCommand(
            siteId, new DecideSiteRequest(Approved: true), owners.EmployeeId, SiteTestData.OwnerUserId));

        await using var scope = fixture.CreateScope();
        var shared = scope.ServiceProvider.GetRequiredService<SharedDbContext>();

        var types = await shared.OutboxMessages
            .AsNoTracking()
            .Where(m => m.Type.Contains("Site"))
            .Select(m => m.Type)
            .ToListAsync();

        Assert.Contains(types, t => t.Contains(nameof(SiteSubmittedIntegrationEvent)));
        Assert.Contains(types, t => t.Contains(nameof(SiteApprovedIntegrationEvent)));
    }

    // ── Listing ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetSites_ClampsThePageSizeAndFiltersByStatus()
    {
        var owners = await SiteTestData.OwnersAsync(fixture);
        var siteId = await CreateSiteAsync();
        await SendAsync(new SubmitSiteCommand(siteId, SiteTestData.OwnerUserId));

        await using var scope = fixture.CreateScope();
        scope.ServiceProvider.GetRequiredService<TestCurrentUser>()
            .Grant(ProjectsPermissions.Projects.ReadAll);

        var handler = scope.ServiceProvider
            .GetRequiredService<IQueryHandler<GetSitesQuery, BuildingBlocks.Pagination.PagedResult<SiteListItemResponse>>>();

        var submitted = await handler.HandleAsync(new GetSitesQuery(
            owners.ProjectId, SiteStatus.Submitted, SiteTestData.OwnerUserId, PageIndex: 0, PageSize: 9999));

        Assert.True(submitted.IsSuccess);

        // Clamped, not trusted: an unbounded page size is a denial-of-service on a large project.
        Assert.Equal(1, submitted.Value!.PageNumber);
        Assert.Equal(200, submitted.Value!.PageSize);
        Assert.Contains(submitted.Value!.Items, s => s.Id == siteId);
        Assert.All(submitted.Value!.Items, s => Assert.Equal(SiteStatus.Submitted, s.Status));
    }
}
