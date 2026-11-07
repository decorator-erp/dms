using BuildingBlocks.Messaging;
using BuildingBlocks.Outbox;
using BuildingBlocks.Pagination;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Projects.Contracts;
using SharedKernel;
using Sites.Contracts;
using Sites.Contracts.Surveys;
using Sites.Domain;
using Xunit;
// The eight enum pairs are duplicated between Contracts and Domain by construction (see
// SiteEnumMapping); these tests speak Contracts, while SiteErrors comes from Domain.
using SiteStatus = Sites.Contracts.SiteStatus;

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
        SiteTestData.Owners owners = await SiteTestData.OwnersAsync(fixture);

        await using AsyncServiceScope scope = fixture.CreateScope();
        scope.ServiceProvider.GetRequiredService<TestCurrentUser>()
            .Grant(ProjectsPermissions.Projects.ReadAll);

        Result<Guid> result = await scope.ServiceProvider
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
        await using AsyncServiceScope scope = fixture.CreateScope();
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

        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync();
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
        Guid first = await CreateSiteAsync();
        Guid second = await CreateSiteAsync();

        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();

        await using var command = new NpgsqlCommand(
            $"""SELECT "Code" FROM sites.sites WHERE "Id" IN ('{first}','{second}') ORDER BY "Code";""",
            connection);

        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync();
        var codes = new List<string>();
        while (await reader.ReadAsync())
        {
            codes.Add(reader.GetString(0));
        }

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
        await using AsyncServiceScope scope = fixture.CreateScope();
        scope.ServiceProvider.GetRequiredService<TestCurrentUser>()
            .Grant(ProjectsPermissions.Projects.ReadAll);

        Result<Guid> result = await scope.ServiceProvider
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
        Guid siteId = await CreateSiteAsync();

        Assert.True((await SendAsync(new SubmitSiteCommand(siteId, SiteTestData.OwnerEmployeeId, SiteTestData.OwnerUserId))).IsSuccess);

        (int Status, string? Address, DateTime? SubmittedAt, bool IsActive) row = await ReadRowAsync(siteId);
        Assert.Equal(2, row.Status);
        Assert.NotNull(row.SubmittedAt);
    }

    [Fact]
    public async Task Submit_WithoutAnAddress_IsRefusedAndPersistsNothing()
    {
        Guid siteId = await CreateSiteAsync(address: null);

        Result result = await SendAsync(new SubmitSiteCommand(siteId, SiteTestData.OwnerEmployeeId, SiteTestData.OwnerUserId));

        Assert.Equal(SiteErrors.AddressRequiredToSubmit, result.Error);

        (int Status, string? Address, DateTime? SubmittedAt, bool IsActive) row = await ReadRowAsync(siteId);
        Assert.Equal(1, row.Status);
        Assert.Null(row.SubmittedAt);
    }

    [Fact]
    public async Task Approve_PersistsTheDecisionAndLocksTheSurvey()
    {
        Guid siteId = await CreateSiteAsync();
        SiteTestData.Owners owners = await SiteTestData.OwnersAsync(fixture);

        Assert.True((await SendAsync(new SubmitSiteCommand(siteId, SiteTestData.OwnerEmployeeId, SiteTestData.OwnerUserId))).IsSuccess);
        Assert.True((await SendAsync(new DecideSiteCommand(
            siteId, new DecideSiteRequest(Approved: true, "Checked."),
            owners.EmployeeId, SiteTestData.OwnerUserId))).IsSuccess);

        Assert.Equal(3, (await ReadRowAsync(siteId)).Status);

        // And the lock is real: a header edit is now refused.
        Result update = await SendAsync(new UpdateSiteCommand(
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
        Guid siteId = await CreateSiteAsync();
        SiteTestData.Owners owners = await SiteTestData.OwnersAsync(fixture);

        Assert.True((await SendAsync(new SubmitSiteCommand(siteId, SiteTestData.OwnerEmployeeId, SiteTestData.OwnerUserId))).IsSuccess);

        Result blocked = await SendAsync(new UpdateSiteCommand(
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
        Guid siteId = await CreateSiteAsync();
        SiteTestData.Owners owners = await SiteTestData.OwnersAsync(fixture);

        Assert.True((await SendAsync(new SubmitSiteCommand(siteId, SiteTestData.OwnerEmployeeId, SiteTestData.OwnerUserId))).IsSuccess);
        Assert.True((await SendAsync(new DecideSiteCommand(
            siteId, new DecideSiteRequest(Approved: false, "Rework the openings."),
            owners.EmployeeId, SiteTestData.OwnerUserId))).IsSuccess);

        Assert.Equal(4, (await ReadRowAsync(siteId)).Status);

        Assert.True((await SendAsync(new ReopenSiteCommand(siteId, SiteTestData.OwnerUserId))).IsSuccess);

        (int Status, string? Address, DateTime? SubmittedAt, bool IsActive) row = await ReadRowAsync(siteId);
        Assert.Equal(1, row.Status);

        // All four stamps cleared -- the row would violate ck_sites_status_stamps otherwise, so this
        // passing at all is the constraint agreeing with the entity.
        Assert.Null(row.SubmittedAt);
    }

    [Fact]
    public async Task Update_PersistsTheChange()
    {
        Guid siteId = await CreateSiteAsync();

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
        Guid siteId = await CreateSiteAsync();
        SiteTestData.Owners owners = await SiteTestData.OwnersAsync(fixture);

        await SendAsync(new SubmitSiteCommand(siteId, SiteTestData.OwnerEmployeeId, SiteTestData.OwnerUserId));
        await SendAsync(new DecideSiteCommand(
            siteId, new DecideSiteRequest(Approved: true), owners.EmployeeId, SiteTestData.OwnerUserId));

        Result result = await SendAsync(new DeleteSiteCommand(siteId, SiteTestData.OwnerUserId));

        Assert.Equal(SiteErrors.SiteLocked, result.Error);
        Assert.True((await ReadRowAsync(siteId)).IsActive);
    }

    [Fact]
    public async Task Delete_OnADraftSurvey_SoftDeletesIt()
    {
        Guid siteId = await CreateSiteAsync();

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
        Guid siteId = await CreateSiteAsync();
        SiteTestData.Owners owners = await SiteTestData.OwnersAsync(fixture);

        await SendAsync(new SubmitSiteCommand(siteId, SiteTestData.OwnerEmployeeId, SiteTestData.OwnerUserId));
        await SendAsync(new DecideSiteCommand(
            siteId, new DecideSiteRequest(Approved: true), owners.EmployeeId, SiteTestData.OwnerUserId));

        await using AsyncServiceScope scope = fixture.CreateScope();
        SharedDbContext shared = scope.ServiceProvider.GetRequiredService<SharedDbContext>();

        List<string> types = await shared.OutboxMessages
            .AsNoTracking()
            .Where(m => m.Type.Contains("Site"))
            .Select(m => m.Type)
            .ToListAsync();

        Assert.Contains(types, t => t.Contains(nameof(SiteSubmittedIntegrationEvent)));
        Assert.Contains(types, t => t.Contains(nameof(SiteApprovedIntegrationEvent)));
    }

    // ── Listing ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetSites_PagesAndFiltersByStatus()
    {
        SiteTestData.Owners owners = await SiteTestData.OwnersAsync(fixture);
        Guid siteId = await CreateSiteAsync();
        await SendAsync(new SubmitSiteCommand(siteId, SiteTestData.OwnerEmployeeId, SiteTestData.OwnerUserId));

        await using AsyncServiceScope scope = fixture.CreateScope();
        scope.ServiceProvider.GetRequiredService<TestCurrentUser>()
            .Grant(ProjectsPermissions.Projects.ReadAll);

        IQueryHandler<GetSitesQuery, PagedResult<SiteListItemResponse>> handler = scope.ServiceProvider
            .GetRequiredService<IQueryHandler<GetSitesQuery, BuildingBlocks.Pagination.PagedResult<SiteListItemResponse>>>();

        Result<PagedResult<SiteListItemResponse>> submitted = await handler.HandleAsync(new GetSitesQuery(
            owners.ProjectId, SiteStatus.Submitted, SiteTestData.OwnerUserId, PageIndex: 1, PageSize: 200));

        Assert.True(submitted.IsSuccess);

        // Out-of-range paging is a 400 now (S8 -- see S8_OutOfRangePagingOrStatus_IsAValidationError).
        Assert.Equal(1, submitted.Value!.PageNumber);
        Assert.Equal(200, submitted.Value!.PageSize);
        Assert.Contains(submitted.Value!.Items, s => s.Id == siteId);
        Assert.All(submitted.Value!.Items, s => Assert.Equal(SiteStatus.Submitted, s.Status));
    }

    /// <summary>
    /// <b>S8.</b> Paging was clamped rather than validated, and the page index was not bounded at all:
    /// <c>(PageIndex - 1) * PageSize</c> overflows <c>int</c> long before <c>int.MaxValue</c>, which
    /// reached the database as a negative OFFSET (500). Now <c>GetProjectsQueryValidator</c>'s bounds —
    /// page 1..1,000,000, size 1..200 — and an unknown status number are each a 400.
    /// </summary>
    [Theory]
    [InlineData(0, 25, 1)]
    [InlineData(1_000_001, 25, 1)]
    [InlineData(int.MaxValue, 200, 1)]
    [InlineData(1, 0, 1)]
    [InlineData(1, 201, 1)]
    [InlineData(1, 25, 99)]
    public async Task S8_OutOfRangePagingOrStatus_IsAValidationError(int pageIndex, int pageSize, int status)
    {
        SiteTestData.Owners owners = await SiteTestData.OwnersAsync(fixture);

        await using AsyncServiceScope scope = fixture.CreateScope();
        scope.ServiceProvider.GetRequiredService<TestCurrentUser>()
            .Grant(ProjectsPermissions.Projects.ReadAll);

        Result<PagedResult<SiteListItemResponse>> result = await scope.ServiceProvider
            .GetRequiredService<IQueryHandler<GetSitesQuery, PagedResult<SiteListItemResponse>>>()
            .HandleAsync(new GetSitesQuery(
                owners.ProjectId, (SiteStatus)status, SiteTestData.OwnerUserId, pageIndex, pageSize));

        Assert.IsType<ValidationError>(result.Error);
    }

    /// <summary>
    /// <b>S11 / NS4.</b> The rejection notice told the surveyor the survey "has been reopened for
    /// rework" — it has not; a rejected survey stays Rejected until someone reopens it. And the note was
    /// read from the ROW when the event was processed, so a reopen in between (which clears
    /// <c>DecisionNote</c>) delivered a rejection with no reason. The note now travels in the payload.
    /// </summary>
    [Fact]
    public async Task S11_RejectionNotice_IsAccurateAndKeepsTheNoteAfterAReopen()
    {
        Guid siteId = await CreateSiteAsync();
        SiteTestData.Owners owners = await SiteTestData.OwnersAsync(fixture);

        Assert.True((await SendAsync(new SubmitSiteCommand(
            siteId, SiteTestData.OwnerEmployeeId, SiteTestData.OwnerUserId))).IsSuccess);
        Assert.True((await SendAsync(new DecideSiteCommand(
            siteId, new DecideSiteRequest(Approved: false, Note: "Kitchen wall missing."),
            owners.EmployeeId, SiteTestData.OwnerUserId))).IsSuccess);

        // Reopened BEFORE the outbox delivers the decision -- clears the row's DecisionNote.
        Assert.True((await SendAsync(new ReopenSiteCommand(siteId, SiteTestData.OwnerUserId))).IsSuccess);

        await fixture.OutboxProcessor.ProcessPendingAsync(CancellationToken.None);

        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            """
            SELECT "Body" FROM notify.notifications
            WHERE "RecipientUserId" = @user AND "DedupeKey" LIKE @key
            """, connection);
        command.Parameters.AddWithValue("user", SiteTestData.OwnerUserId);
        command.Parameters.AddWithValue("key", $"site-{siteId}-decided%");

        string body = Assert.IsType<string>(await command.ExecuteScalarAsync());

        Assert.Contains("Kitchen wall missing.", body, StringComparison.Ordinal);
        Assert.DoesNotContain("has been reopened", body, StringComparison.Ordinal);
    }

    /// <summary>
    /// <b>B-S-M7.</b> A survey could be started on a deactivated, Completed or Cancelled project — the
    /// only check was that the project exists.
    /// </summary>
    [Theory]
    [InlineData(4, true)]
    [InlineData(5, true)]
    [InlineData(2, false)]
    public async Task BSM7_CreatingASiteOnAClosedOrInactiveProject_IsProjectClosed(int status, bool isActive)
    {
        SiteTestData.Owners owners = await SiteTestData.OwnersAsync(fixture);
        Guid projectId = await SiteTestData.CreateProjectInStateAsync(fixture, status, isActive);

        await using AsyncServiceScope scope = fixture.CreateScope();
        scope.ServiceProvider.GetRequiredService<TestCurrentUser>()
            .Grant(ProjectsPermissions.Projects.ReadAll);

        Result<Guid> result = await scope.ServiceProvider
            .GetRequiredService<ICommandHandler<CreateSiteCommand, Guid>>()
            .HandleAsync(new CreateSiteCommand(
                new CreateSiteRequest(projectId, new SiteUnitDataRequest(Address: "1 Closed St")),
                owners.EmployeeId, SiteTestData.OwnerUserId));

        Assert.Equal(SiteErrors.ProjectClosed, result.Error);
        Assert.Equal(409, result.Error.Status);
    }

    /// <summary>
    /// <b>B-S-L3.</b> A survey decision was filed as <c>StageCompleted</c> / <c>StageRejected</c>, so a
    /// client filtering or routing by type sent it to the wrong place. Sites has its own pair now.
    /// </summary>
    [Fact]
    public async Task BSL3_SurveyDecisions_UseTheSitesNotificationTypes()
    {
        SiteTestData.Owners owners = await SiteTestData.OwnersAsync(fixture);
        Guid approvedSite = await CreateSiteAsync();
        Guid rejectedSite = await CreateSiteAsync();

        foreach ((Guid siteId, bool approved) in new[] { (approvedSite, true), (rejectedSite, false) })
        {
            Assert.True((await SendAsync(new SubmitSiteCommand(
                siteId, SiteTestData.OwnerEmployeeId, SiteTestData.OwnerUserId))).IsSuccess);
            Assert.True((await SendAsync(new DecideSiteCommand(
                siteId, new DecideSiteRequest(approved), owners.EmployeeId, SiteTestData.OwnerUserId))).IsSuccess);
        }

        await fixture.OutboxProcessor.ProcessPendingAsync(CancellationToken.None);

        Assert.Equal((int)Notifications.Contracts.NotificationType.SiteApproved, await NotificationTypeAsync(approvedSite));
        Assert.Equal((int)Notifications.Contracts.NotificationType.SiteRejected, await NotificationTypeAsync(rejectedSite));
    }

    /// <summary><b>B-S-L3.</b> A decision event whose site is gone was dropped without a trace.</summary>
    [Fact]
    public async Task BSL3_ADecisionForAMissingSite_IsLoggedNotSilentlyDropped()
    {
        await using AsyncServiceScope scope = fixture.CreateScope();
        IServiceProvider sp = scope.ServiceProvider;
        var logger = new CapturingLogger<Sites.Infrastructure.IntegrationEvents.SiteApprovedNotificationHandler>();

        var handler = new Sites.Infrastructure.IntegrationEvents.SiteApprovedNotificationHandler(
            sp.GetRequiredService<Sites.Infrastructure.Persistence.SitesDbContext>(),
            sp.GetRequiredService<Hr.Contracts.IHrDirectory>(),
            sp.GetRequiredService<Notifications.Contracts.INotificationService>(),
            logger);

        await handler.HandleAsync(
            new SiteApprovedIntegrationEvent(
                Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), true, Guid.NewGuid(), DateTimeOffset.UtcNow),
            CancellationToken.None);

        Assert.Contains(logger.Entries, e => e.Level == Microsoft.Extensions.Logging.LogLevel.Warning);
    }

    private async Task<int> NotificationTypeAsync(Guid siteId)
    {
        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            """SELECT "Type" FROM notify.notifications WHERE "DedupeKey" LIKE @key""", connection);
        command.Parameters.AddWithValue("key", $"site-{siteId}-decided%");
        return Convert.ToInt32(await command.ExecuteScalarAsync(), System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// <b>B-S-L1 (C11).</b> Nothing stopped a survey's creator approving it themselves, making the
    /// Draft → Submitted → Approved lifecycle decorative for anyone holding <c>sites.approve</c>. Refused
    /// with 409 unless the approver holds the project-wide override <c>project.read_all</c> (which the
    /// full-access admin/manager roles carry). Rejecting one's own survey stays allowed.
    /// </summary>
    [Fact]
    public async Task BSL1_TheCreatorApprovingTheirOwnSurvey_IsRefusedWithoutTheOverride()
    {
        SiteTestData.Owners owners = await SiteTestData.OwnersAsync(fixture);
        Guid siteId = await CreateSiteAsync();
        Assert.True((await SendAsync(new SubmitSiteCommand(
            siteId, SiteTestData.OwnerEmployeeId, SiteTestData.OwnerUserId))).IsSuccess);

        // The owner reaches the site as the project's owner -- no project.read_all.
        Result refused;
        await using (AsyncServiceScope scope = fixture.CreateScope())
        {
            scope.ServiceProvider.GetRequiredService<TestCurrentUser>().UserId = SiteTestData.OwnerUserId;
            refused = await scope.ServiceProvider
                .GetRequiredService<ICommandHandler<DecideSiteCommand>>()
                .HandleAsync(new DecideSiteCommand(
                    siteId, new DecideSiteRequest(Approved: true), owners.EmployeeId, SiteTestData.OwnerUserId));
        }

        Assert.Equal(SiteErrors.SelfApprovalNotAllowed, refused.Error);
        Assert.Equal(409, refused.Error.Status);
        Assert.Equal(2, (await ReadRowAsync(siteId)).Status);

        // With the override (SendAsync grants project.read_all), the same approval goes through.
        Assert.True((await SendAsync(new DecideSiteCommand(
            siteId, new DecideSiteRequest(Approved: true), owners.EmployeeId, SiteTestData.OwnerUserId))).IsSuccess);
    }
}

internal sealed class CapturingLogger<T> : Microsoft.Extensions.Logging.ILogger<T>
{
    public List<(Microsoft.Extensions.Logging.LogLevel Level, string Message)> Entries { get; } = [];

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(Microsoft.Extensions.Logging.LogLevel logLevel) => true;

    public void Log<TState>(
        Microsoft.Extensions.Logging.LogLevel logLevel, Microsoft.Extensions.Logging.EventId eventId,
        TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
        Entries.Add((logLevel, formatter(state, exception)));
}
