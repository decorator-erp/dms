using Approvals.Contracts;
using Approvals.Contracts.Approvals;
using BuildingBlocks.Messaging;
using BuildingBlocks.Outbox;
using BuildingBlocks.Pagination;
using Crm.Application.Features.Briefs;
using Crm.Contracts;
using Crm.Contracts.Briefs;
using Crm.Domain;
using Crm.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Notifications.Contracts;
using SharedKernel;
using Xunit;
using ApprovalDecision = Approvals.Contracts.ApprovalDecision;
using ApprovalState = Crm.Domain.ApprovalState;
using CrmBriefHeadData = Crm.Contracts.Briefs.CrmBriefHeadData;
using ProjectSector = Crm.Contracts.ProjectSector;
using UnitCondition = Crm.Contracts.UnitCondition;
using UnitType = Crm.Contracts.UnitType;

namespace Crm.Application.Tests;

/// <summary>
/// Run A of the CRM brief fixes (H1/H3/H4/M3): every round is bound to its approval, the send flushes
/// CRM before it requests, the approval id is deterministic per round, and the office sender is the
/// requester.
///
/// <para><b>M6 is NOT exercised here.</b> These tests decide through the real
/// <see cref="DecideApprovalCommand"/> handler with the client as the current user, which proves the
/// approval→CRM path. They do not prove the client can reach that path over HTTP: the route requires
/// <c>approvals.decide</c>, which the seeded Client role does not hold (see CRM-BRIEF-FIX-NOTES.md, M6).
/// Hence the <c>ClientDecidesViaHandler_NotPortalRoute</c> naming.</para>
/// </summary>
[Collection(CrmApplicationCollection.Name)]
public sealed class BriefRoundFlowTests(CrmApplicationFixture fixture)
{
    private static readonly CrmBriefHeadData Head = new((UnitType)1, (UnitCondition)1, Address: "Round Addr");

    [Fact]
    public async Task Send_ThenApprove_ClientDecidesViaHandler_NotPortalRoute_ApprovesEnqueuesAndSatisfiesTheGate()
    {
        await using AsyncServiceScope scope = fixture.CreateScope();
        IServiceProvider sp = scope.ServiceProvider;
        BriefTestProject p = await BriefTestSeeding.CreateProjectWithStageAsync(sp, "round-approve");
        Guid briefId = await CreateBriefAsync(sp, p);

        Assert.True((await SendAsync(p, briefId)).IsSuccess);

        CrmDesignBrief pending = await LoadBriefAsync(briefId);
        Assert.Equal(ApprovalState.PendingApproval, pending.Status);
        Guid approvalId = CrmApprovalIds.For(ApprovalSubjectType.DesignBrief, briefId, 1);
        Assert.Equal(approvalId, pending.PendingApprovalId);

        Assert.True((await DecideAsClientAsync(p, approvalId, ApprovalDecision.Approved)).IsSuccess);
        await DrainOutboxAsync();

        CrmDesignBrief approved = await LoadBriefAsync(briefId);
        Assert.Equal(ApprovalState.Approved, approved.Status);
        Assert.Null(approved.PendingApprovalId);
        Assert.True(await OutboxHasAsync(nameof(CrmBriefApprovedIntegrationEvent), briefId));

        await using AsyncServiceScope readScope = fixture.CreateScope();
        ICrmGateReader gateReader = readScope.ServiceProvider.GetRequiredService<ICrmGateReader>();
        Assert.True(await gateReader.IsBriefApprovedForStageAsync(p.StageId, CancellationToken.None));

        // H4: the requester is the office sender, so the decision notification reaches the office.
        IQueryHandler<GetMyNotificationsQuery, PagedResult<NotificationResponse>> notifications =
            readScope.ServiceProvider.GetRequiredService<IQueryHandler<GetMyNotificationsQuery, PagedResult<NotificationResponse>>>();
        PagedResult<NotificationResponse> inbox = (await notifications.HandleAsync(
            new GetMyNotificationsQuery(p.OwnerAppUserId, UnreadOnly: false, PageNumber: 1, PageSize: 50))).Value!;
        Assert.Contains(inbox.Items, n => n.EntityId == approvalId);
    }

    [Fact]
    public async Task Reject_ClientDecidesViaHandler_NotPortalRoute_ThenEditGoesToDraft_ThenResendOpensRoundTwo()
    {
        await using AsyncServiceScope scope = fixture.CreateScope();
        IServiceProvider sp = scope.ServiceProvider;
        BriefTestProject p = await BriefTestSeeding.CreateProjectWithStageAsync(sp, "round-reject");
        Guid briefId = await CreateBriefAsync(sp, p);

        Assert.True((await SendAsync(p, briefId)).IsSuccess);
        Guid roundOne = CrmApprovalIds.For(ApprovalSubjectType.DesignBrief, briefId, 1);
        Assert.True((await DecideAsClientAsync(p, roundOne, ApprovalDecision.Rejected, "Wrong address")).IsSuccess);
        await DrainOutboxAsync();
        Assert.Equal(ApprovalState.Rejected, (await LoadBriefAsync(briefId)).Status);

        Assert.True((await UpdateHeadAsync(p, briefId, Head with { Address = "Fixed Addr" })).IsSuccess);
        Assert.Equal(ApprovalState.Draft, (await LoadBriefAsync(briefId)).Status);

        Assert.True((await SendAsync(p, briefId)).IsSuccess);

        CrmDesignBrief resent = await LoadBriefAsync(briefId);
        Assert.Equal(ApprovalState.PendingApproval, resent.Status);
        Assert.Equal(CrmApprovalIds.For(ApprovalSubjectType.DesignBrief, briefId, 2), resent.PendingApprovalId);
        Assert.Equal(2, await CountSnapshotsAsync(briefId));
    }

    [Fact]
    public async Task RoundDiff_ShowsASectorChange()
    {
        await using AsyncServiceScope scope = fixture.CreateScope();
        IServiceProvider sp = scope.ServiceProvider;
        BriefTestProject p = await BriefTestSeeding.CreateProjectWithStageAsync(sp, "round-sector-diff");
        Guid briefId = await CreateBriefAsync(sp, p);
        Assert.True((await UpdateHeadAsync(p, briefId, Head with { Sector = ProjectSector.Medical })).IsSuccess);

        Assert.True((await SendAsync(p, briefId)).IsSuccess);
        Guid roundOne = CrmApprovalIds.For(ApprovalSubjectType.DesignBrief, briefId, 1);
        Assert.True((await DecideAsClientAsync(p, roundOne, ApprovalDecision.Rejected, "Wrong sector")).IsSuccess);
        await DrainOutboxAsync();
        Assert.True((await UpdateHeadAsync(p, briefId, Head with { Sector = ProjectSector.Residential })).IsSuccess);
        Assert.True((await SendAsync(p, briefId)).IsSuccess);

        await using AsyncServiceScope diffScope = OwnerScope(p);
        Result<BriefHeadDiffResponse> diff = await diffScope.ServiceProvider
            .GetRequiredService<IQueryHandler<GetCrmBriefRoundDiffQuery, BriefHeadDiffResponse>>()
            .HandleAsync(new GetCrmBriefRoundDiffQuery(briefId, 2, p.OwnerAppUserId));

        Assert.True(diff.IsSuccess, diff.IsFailure ? diff.Error.ToString() : null);
        BriefFieldChangeResponse change = Assert.Single(diff.Value!.Changes);
        Assert.Equal("Sector", change.Field);
        Assert.Equal(((int)ProjectSector.Medical).ToString(System.Globalization.CultureInfo.InvariantCulture), change.PreviousValue);
        Assert.Equal(((int)ProjectSector.Residential).ToString(System.Globalization.CultureInfo.InvariantCulture), change.CurrentValue);
    }

    [Fact]
    public async Task Rounds_list_changes_requested_voided_and_approved_and_the_portal_hides_the_voided_round()
    {
        await using AsyncServiceScope scope = fixture.CreateScope();
        IServiceProvider sp = scope.ServiceProvider;
        BriefTestProject p = await BriefTestSeeding.CreateProjectWithStageAsync(sp, "rounds-list");
        Guid briefId = await CreateBriefAsync(sp, p);

        // Round 1: the client asks for changes.
        Assert.True((await SendAsync(p, briefId)).IsSuccess);
        Assert.True((await DecideAsClientAsync(p, CrmApprovalIds.For(ApprovalSubjectType.DesignBrief, briefId, 1), ApprovalDecision.Rejected, "Change it")).IsSuccess);
        await DrainOutboxAsync();
        Assert.True((await UpdateHeadAsync(p, briefId, Head with { Address = "Fixed Addr" })).IsSuccess);

        // Round 2: its approval was decided before CRM knew of it, so the send voids it and opens round 3.
        Guid roundTwo = CrmApprovalIds.For(ApprovalSubjectType.DesignBrief, briefId, 2);
        Assert.True((await RequestDirectlyAsync(sp, p, roundTwo, briefId)).IsSuccess);
        Assert.True((await DecideAsClientAsync(p, roundTwo, ApprovalDecision.Approved, "Unseen")).IsSuccess);
        await DrainOutboxAsync();
        Assert.True((await SendAsync(p, briefId)).IsSuccess);
        Assert.Equal(
            [CrmBriefRoundOutcome.ChangesRequested, CrmBriefRoundOutcome.Voided, CrmBriefRoundOutcome.Pending],
            (await OfficeRoundsAsync(p, briefId)).Value!.Select(r => r.Outcome));

        // Round 3: approved.
        Assert.True((await DecideAsClientAsync(p, CrmApprovalIds.For(ApprovalSubjectType.DesignBrief, briefId, 3), ApprovalDecision.Approved)).IsSuccess);
        await DrainOutboxAsync();

        IReadOnlyList<CrmBriefRoundResponse> office = (await OfficeRoundsAsync(p, briefId)).Value!;
        Assert.Equal([1, 2, 3], office.Select(r => r.Round));
        Assert.Equal(
            [CrmBriefRoundOutcome.ChangesRequested, CrmBriefRoundOutcome.Voided, CrmBriefRoundOutcome.Approved],
            office.Select(r => r.Outcome));
        Assert.All(office, r => Assert.NotNull(r.DecidedAtUtc));
        Assert.True(office[0].SentAtUtc <= office[1].SentAtUtc && office[1].SentAtUtc <= office[2].SentAtUtc);

        Result<IReadOnlyList<CrmBriefRoundResponse>> portal = await PortalRoundsAsync(p.ClientAppUserId, briefId);
        Assert.True(portal.IsSuccess, portal.IsFailure ? portal.Error.ToString() : null);
        Assert.Equal([1, 3], portal.Value!.Select(r => r.Round));
        Assert.Equal([CrmBriefRoundOutcome.ChangesRequested, CrmBriefRoundOutcome.Approved], portal.Value!.Select(r => r.Outcome));
    }

    [Fact]
    public async Task Rounds_are_404_for_another_client_and_for_an_employee_outside_the_project()
    {
        await using AsyncServiceScope scope = fixture.CreateScope();
        IServiceProvider sp = scope.ServiceProvider;
        BriefTestProject p = await BriefTestSeeding.CreateProjectWithStageAsync(sp, "rounds-scope");
        Guid briefId = await CreateBriefAsync(sp, p);
        Assert.True((await SendAsync(p, briefId)).IsSuccess);
        (_, string otherClient) = await CrmClientSeeding.CreateClientAsync(sp, $"rounds-other-{Guid.NewGuid():N}@example.com");
        (_, string outsider) = await HrTestSeeding.HireEmployeeAsync(sp, $"rounds-outsider-{Guid.NewGuid():N}@example.com");

        Result<IReadOnlyList<CrmBriefRoundResponse>> asOtherClient = await PortalRoundsAsync(otherClient, briefId);
        Assert.Equal(404, asOtherClient.Error.Status);

        await using AsyncServiceScope outsiderScope = fixture.CreateScope();
        outsiderScope.ServiceProvider.GetRequiredService<TestCurrentUser>().UserId = outsider;
        Result<IReadOnlyList<CrmBriefRoundResponse>> asOutsider = await outsiderScope.ServiceProvider
            .GetRequiredService<IQueryHandler<GetCrmBriefRoundsQuery, IReadOnlyList<CrmBriefRoundResponse>>>()
            .HandleAsync(new GetCrmBriefRoundsQuery(briefId, outsider));
        Assert.Equal(CrmBriefErrors.NotFound, asOutsider.Error);

        Assert.Equal([CrmBriefRoundOutcome.Pending], (await OfficeRoundsAsync(p, briefId)).Value!.Select(r => r.Outcome));
    }

    [Fact]
    public async Task EditWhilePending_IsRejectedWith409()
    {
        await using AsyncServiceScope scope = fixture.CreateScope();
        IServiceProvider sp = scope.ServiceProvider;
        BriefTestProject p = await BriefTestSeeding.CreateProjectWithStageAsync(sp, "round-locked");
        Guid briefId = await CreateBriefAsync(sp, p);
        Assert.True((await SendAsync(p, briefId)).IsSuccess);

        Result result = await UpdateHeadAsync(p, briefId, Head with { Address = "After send" });

        Assert.Equal(CrmBriefErrors.HeadLockedPendingApproval, result.Error);
        Assert.Equal(409, result.Error.Status);
        Assert.Equal("Round Addr", (await LoadBriefAsync(briefId)).Address);
    }

    [Fact]
    public async Task SecondSendWhilePending_IsRejectedWith409()
    {
        await using AsyncServiceScope scope = fixture.CreateScope();
        IServiceProvider sp = scope.ServiceProvider;
        BriefTestProject p = await BriefTestSeeding.CreateProjectWithStageAsync(sp, "round-twice");
        Guid briefId = await CreateBriefAsync(sp, p);
        Assert.True((await SendAsync(p, briefId)).IsSuccess);

        Result second = await SendAsync(p, briefId);

        Assert.Equal(CrmBriefErrors.NotSendable, second.Error);
        Assert.Equal(409, second.Error.Status);
        Assert.Equal(1, await CountSnapshotsAsync(briefId));
    }

    [Fact]
    public async Task FailedApprovalRequest_LeavesNoSnapshot_AndThePriorStatus()
    {
        await using AsyncServiceScope scope = fixture.CreateScope();
        IServiceProvider sp = scope.ServiceProvider;
        BriefTestProject p = await BriefTestSeeding.CreateProjectWithStageAsync(sp, "round-fail");
        Guid briefId = await CreateBriefAsync(sp, p);

        // An unrelated open approval on the same subject makes the real request fail (OpenApprovalExists).
        Assert.True((await RequestDirectlyAsync(sp, p, Guid.NewGuid(), briefId)).IsSuccess);

        Result result = await SendAsync(p, briefId);

        Assert.True(result.IsFailure);
        Assert.Equal("approvals.OpenApprovalExists", result.Error.Code);
        CrmDesignBrief after = await LoadBriefAsync(briefId);
        Assert.Equal(ApprovalState.Draft, after.Status);
        Assert.Null(after.PendingApprovalId);
        Assert.Equal(0, await CountSnapshotsAsync(briefId));
    }

    /// <summary>The H3 window: the approval committed, CRM's commit did not. The retry recomputes round 1,
    /// hence the same id, and lands on the existing approval instead of an <c>OpenApprovalExists</c>.</summary>
    [Fact]
    public async Task Resend_AfterCrmCommitWasLost_ReusesTheSameApproval()
    {
        await using AsyncServiceScope scope = fixture.CreateScope();
        IServiceProvider sp = scope.ServiceProvider;
        BriefTestProject p = await BriefTestSeeding.CreateProjectWithStageAsync(sp, "round-lost-commit");
        Guid briefId = await CreateBriefAsync(sp, p);
        Guid roundOne = CrmApprovalIds.For(ApprovalSubjectType.DesignBrief, briefId, 1);
        Assert.True((await RequestDirectlyAsync(sp, p, roundOne, briefId)).IsSuccess);

        Assert.True((await SendAsync(p, briefId)).IsSuccess);

        CrmDesignBrief after = await LoadBriefAsync(briefId);
        Assert.Equal(ApprovalState.PendingApproval, after.Status);
        Assert.Equal(roundOne, after.PendingApprovalId);
        Assert.Equal(1, await CountSnapshotsAsync(briefId));
    }

    /// <summary>The orphan was decided while round 1's snapshot had rolled back, i.e. on content the client
    /// never saw. A missed rejection is safe to apply; a missed approval is not — round 1 is voided and the
    /// head goes out again as round 2 under its own deterministic id.</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Send_WhenOrphanApprovalAlreadyDecided_AppliesRejection_ButVoidsAnApproval(bool approve)
    {
        ApprovalDecision decision = approve ? ApprovalDecision.Approved : ApprovalDecision.Rejected;
        await using AsyncServiceScope scope = fixture.CreateScope();
        IServiceProvider sp = scope.ServiceProvider;
        BriefTestProject p = await BriefTestSeeding.CreateProjectWithStageAsync(sp, "round-orphan");
        Guid briefId = await CreateBriefAsync(sp, p);
        Guid roundOne = CrmApprovalIds.For(ApprovalSubjectType.DesignBrief, briefId, 1);
        Assert.True((await RequestDirectlyAsync(sp, p, roundOne, briefId)).IsSuccess);
        Assert.True((await DecideAsClientAsync(p, roundOne, decision, "Decided before CRM knew")).IsSuccess);

        // The decided event reaches CRM while the brief is still Draft with no bound approval: ignored.
        await DrainOutboxAsync();
        Assert.Equal(ApprovalState.Draft, (await LoadBriefAsync(briefId)).Status);

        Assert.True((await SendAsync(p, briefId)).IsSuccess);

        CrmDesignBrief after = await LoadBriefAsync(briefId);
        Assert.False(await OutboxHasAsync(nameof(CrmBriefApprovedIntegrationEvent), briefId));
        if (!approve)
        {
            Assert.Equal(ApprovalState.Rejected, after.Status);
            Assert.Null(after.PendingApprovalId);
            Assert.Equal(1, await CountSnapshotsAsync(briefId));
            return;
        }

        Guid roundTwo = CrmApprovalIds.For(ApprovalSubjectType.DesignBrief, briefId, 2);
        Assert.Equal(ApprovalState.PendingApproval, after.Status);
        Assert.Equal(roundTwo, after.PendingApprovalId);
        Assert.Equal(2, await CountSnapshotsAsync(briefId));
        ApprovalSnapshot? fresh = await sp.GetRequiredService<IApprovalRequester>().GetAsync(roundTwo, CancellationToken.None);
        Assert.NotNull(fresh);
        Assert.Null(fresh.Decision);

        // Only the client's decision on round 2 — content they were shown — approves the brief.
        Assert.True((await DecideAsClientAsync(p, roundTwo, ApprovalDecision.Approved)).IsSuccess);
        await DrainOutboxAsync();
        Assert.Equal(ApprovalState.Approved, (await LoadBriefAsync(briefId)).Status);
    }

    [Fact]
    public async Task BriefSpace_SendWhenOrphanApprovalAlreadyApproved_VoidsItAndSendsAgain()
    {
        await using AsyncServiceScope scope = fixture.CreateScope();
        IServiceProvider sp = scope.ServiceProvider;
        BriefTestProject p = await BriefTestSeeding.CreateProjectWithStageAsync(sp, "space-orphan");
        Guid briefId = await CreateBriefAsync(sp, p);
        Guid entryId = (await sp.GetRequiredService<ICommandHandler<CreateCrmBriefSpaceCommand, Guid>>()
            .HandleAsync(new CreateCrmBriefSpaceCommand(briefId, p.EmployeeId, p.OwnerAppUserId, SpaceId: null, Requirements: "light"))).Value;
        Guid sendOne = CrmApprovalIds.For(ApprovalSubjectType.BriefSpace, entryId, 1);
        Assert.True((await sp.GetRequiredService<IApprovalRequester>().RequestAsync(
            new ApprovalRequest(
                sendOne, ApprovalSubjectType.BriefSpace, entryId, ApprovalType.ClientReview,
                p.OwnerAppUserId, [new ApproverSpec(ApproverKind.Client, p.ClientAppUserId)], DueAtUtc: null),
            CancellationToken.None)).IsSuccess);
        Assert.True((await DecideAsClientAsync(p, sendOne, ApprovalDecision.Approved)).IsSuccess);
        await DrainOutboxAsync();

        await using (AsyncServiceScope sendScope = OwnerScope(p))
        {
            Assert.True((await sendScope.ServiceProvider.GetRequiredService<ICommandHandler<SendCrmBriefSpaceCommand>>()
                .HandleAsync(new SendCrmBriefSpaceCommand(entryId, p.OwnerAppUserId))).IsSuccess);
        }

        await using AsyncServiceScope readScope = fixture.CreateScope();
        CrmBriefSpace entry = await readScope.ServiceProvider.GetRequiredService<CrmDbContext>()
            .CrmBriefSpaces.AsNoTracking().SingleAsync(e => e.Id == entryId);
        Assert.Equal(ApprovalState.PendingApproval, entry.ApprovalStatus);
        Assert.Equal(2, entry.SendCount);
        Assert.Equal(CrmApprovalIds.For(ApprovalSubjectType.BriefSpace, entryId, 2), entry.PendingApprovalId);
    }

    /// <summary>Recovery: the brief is PendingApproval under an id Approvals has no row for. Sending
    /// again re-requests that same id rather than refusing forever.</summary>
    [Fact]
    public async Task Send_WhilePendingOnAMissingApproval_ReRequestsTheSameId()
    {
        await using AsyncServiceScope scope = fixture.CreateScope();
        IServiceProvider sp = scope.ServiceProvider;
        BriefTestProject p = await BriefTestSeeding.CreateProjectWithStageAsync(sp, "round-recover");
        Guid briefId = await CreateBriefAsync(sp, p);
        var missing = Guid.NewGuid();
        await using (AsyncServiceScope writeScope = fixture.CreateScope())
        {
            CrmDbContext db = writeScope.ServiceProvider.GetRequiredService<CrmDbContext>();
            CrmDesignBrief brief = await db.CrmDesignBriefs.SingleAsync(b => b.Id == briefId);
            Assert.True(brief.BeginRound(missing).IsSuccess);
            await db.SaveChangesAsync();
        }

        Assert.True((await SendAsync(p, briefId)).IsSuccess);

        ApprovalSnapshot? approval = await sp.GetRequiredService<IApprovalRequester>().GetAsync(missing, CancellationToken.None);
        Assert.NotNull(approval);
        Assert.Equal(briefId, approval.SubjectId);
        Assert.Equal(missing, (await LoadBriefAsync(briefId)).PendingApprovalId);
    }

    [Fact]
    public async Task ConcurrentSends_ExactlyOneWins_TheOtherGets409()
    {
        BriefTestProject p;
        Guid briefId;
        await using (AsyncServiceScope scope = fixture.CreateScope())
        {
            p = await BriefTestSeeding.CreateProjectWithStageAsync(scope.ServiceProvider, "round-race");
            briefId = await CreateBriefAsync(scope.ServiceProvider, p);
        }

        Result[] results = await Task.WhenAll(SendAsync(p, briefId), SendAsync(p, briefId));

        Assert.Single(results, r => r.IsSuccess);
        Result loser = Assert.Single(results, r => r.IsFailure);
        Assert.Equal(409, loser.Error.Status);
        Assert.Equal(1, await CountSnapshotsAsync(briefId));
    }

    /// <summary>M3: a write based on a stale read of the brief row loses to the xmin token, as a 409.</summary>
    [Fact]
    public async Task StaleWrite_OnTheBrief_IsAConcurrencyConflict()
    {
        await using AsyncServiceScope scope = fixture.CreateScope();
        IServiceProvider sp = scope.ServiceProvider;
        BriefTestProject p = await BriefTestSeeding.CreateProjectWithStageAsync(sp, "round-stale");
        Guid briefId = await CreateBriefAsync(sp, p);

        await using AsyncServiceScope staleScope = fixture.CreateScope();
        CrmDbContext staleDb = staleScope.ServiceProvider.GetRequiredService<CrmDbContext>();
        CrmDesignBrief stale = await staleDb.CrmDesignBriefs.SingleAsync(b => b.Id == briefId);

        Assert.True((await UpdateHeadAsync(p, briefId, Head with { Address = "Winner" })).IsSuccess);

        Assert.True(stale.UpdateHead(BriefMapping.ToDomain(Head with { Address = "Loser" }), null).IsSuccess);
        Result result = await BriefWrites.SaveAsync(staleDb, CancellationToken.None);

        Assert.Equal(CrmBriefErrors.ConcurrencyConflict, result.Error);
        Assert.Equal(409, result.Error.Status);
        Assert.Equal("Winner", (await LoadBriefAsync(briefId)).Address);
    }

    [Fact]
    public async Task BriefSpace_SendThenApprove_ClientDecidesViaHandler_NotPortalRoute_BindsTheRound()
    {
        await using AsyncServiceScope scope = fixture.CreateScope();
        IServiceProvider sp = scope.ServiceProvider;
        BriefTestProject p = await BriefTestSeeding.CreateProjectWithStageAsync(sp, "space-round");
        Guid briefId = await CreateBriefAsync(sp, p);
        Guid entryId = (await sp.GetRequiredService<ICommandHandler<CreateCrmBriefSpaceCommand, Guid>>()
            .HandleAsync(new CreateCrmBriefSpaceCommand(briefId, p.EmployeeId, p.OwnerAppUserId, SpaceId: null, Requirements: "light"))).Value;

        await using (AsyncServiceScope sendScope = OwnerScope(p))
        {
            Assert.True((await sendScope.ServiceProvider.GetRequiredService<ICommandHandler<SendCrmBriefSpaceCommand>>()
                .HandleAsync(new SendCrmBriefSpaceCommand(entryId, p.OwnerAppUserId))).IsSuccess);
        }

        Guid approvalId = CrmApprovalIds.For(ApprovalSubjectType.BriefSpace, entryId, 1);

        Assert.True((await DecideAsClientAsync(p, approvalId, ApprovalDecision.Approved)).IsSuccess);
        await DrainOutboxAsync();

        await using AsyncServiceScope readScope = fixture.CreateScope();
        CrmBriefSpace entry = await readScope.ServiceProvider.GetRequiredService<CrmDbContext>()
            .CrmBriefSpaces.AsNoTracking().SingleAsync(e => e.Id == entryId);
        Assert.Equal(ApprovalState.Approved, entry.ApprovalStatus);
        Assert.Null(entry.PendingApprovalId);
        Assert.Equal(1, entry.SendCount);
    }

    /// <summary>D-6: a round sent before outcomes were recorded reads as Unknown. The office still sees it;
    /// the portal hides it unless it is the latest round, as it hides a voided one. Round numbers can then
    /// have gaps on the portal.</summary>
    [Fact]
    public async Task D6_AnOlderRoundWithAnUnknownOutcome_IsHiddenOnThePortal_AndShownInTheOffice()
    {
        await using AsyncServiceScope scope = fixture.CreateScope();
        IServiceProvider sp = scope.ServiceProvider;
        BriefTestProject p = await BriefTestSeeding.CreateProjectWithStageAsync(sp, "rounds-unknown");
        Guid briefId = await CreateBriefAsync(sp, p);

        Assert.True((await SendAsync(p, briefId)).IsSuccess);
        Assert.True((await DecideAsClientAsync(p, CrmApprovalIds.For(ApprovalSubjectType.DesignBrief, briefId, 1), ApprovalDecision.Rejected, "Change it")).IsSuccess);
        await DrainOutboxAsync();
        Assert.True((await UpdateHeadAsync(p, briefId, Head with { Address = "Fixed Addr" })).IsSuccess);
        Assert.True((await SendAsync(p, briefId)).IsSuccess);

        // Round 1 as a brief sent before outcomes were recorded left it: no stored outcome.
        await using (var connection = new Npgsql.NpgsqlConnection(fixture.ConnectionString))
        {
            await connection.OpenAsync();
            await using Npgsql.NpgsqlCommand command = connection.CreateCommand();
            command.CommandText = """UPDATE crm.crm_brief_head_snapshots SET "Outcome" = NULL, "DecidedAtUtc" = NULL WHERE "BriefId" = @id AND "Round" = 1""";
            command.Parameters.AddWithValue("id", briefId);
            Assert.Equal(1, await command.ExecuteNonQueryAsync());
        }

        Assert.Equal(
            [CrmBriefRoundOutcome.Unknown, CrmBriefRoundOutcome.Pending],
            (await OfficeRoundsAsync(p, briefId)).Value!.Select(r => r.Outcome));
        Assert.Equal([2], (await PortalRoundsAsync(p.ClientAppUserId, briefId)).Value!.Select(r => r.Round));
    }

    private static async Task<Guid> CreateBriefAsync(IServiceProvider sp, BriefTestProject p)
    {
        Result<Guid> result = await sp.GetRequiredService<ICommandHandler<CreateCrmDesignBriefCommand, Guid>>()
            .HandleAsync(new CreateCrmDesignBriefCommand(p.ProjectId, p.EmployeeId, p.StageId, Head, p.OwnerAppUserId));
        Assert.True(result.IsSuccess, result.Error?.Description);
        return result.Value;
    }

    /// <summary>A fresh scope per command, acting as the project owner — as each HTTP request would be.
    /// Reusing the seeding scope would hand the handler a stale tracked brief (and a stale xmin).</summary>
    private AsyncServiceScope OwnerScope(BriefTestProject p)
    {
        AsyncServiceScope scope = fixture.CreateScope();
        TestCurrentUser currentUser = scope.ServiceProvider.GetRequiredService<TestCurrentUser>();
        currentUser.UserId = p.OwnerAppUserId;
        currentUser.Grant(Projects.Contracts.ProjectsPermissions.Projects.ReadAll);
        return scope;
    }

    private async Task<Result> SendAsync(BriefTestProject p, Guid briefId)
    {
        await using AsyncServiceScope scope = OwnerScope(p);
        return await scope.ServiceProvider.GetRequiredService<ICommandHandler<SendCrmBriefCommand>>()
            .HandleAsync(new SendCrmBriefCommand(briefId, p.OwnerAppUserId));
    }

    private async Task<Result<IReadOnlyList<CrmBriefRoundResponse>>> OfficeRoundsAsync(BriefTestProject p, Guid briefId)
    {
        await using AsyncServiceScope scope = OwnerScope(p);
        return await scope.ServiceProvider
            .GetRequiredService<IQueryHandler<GetCrmBriefRoundsQuery, IReadOnlyList<CrmBriefRoundResponse>>>()
            .HandleAsync(new GetCrmBriefRoundsQuery(briefId, p.OwnerAppUserId));
    }

    private async Task<Result<IReadOnlyList<CrmBriefRoundResponse>>> PortalRoundsAsync(string clientAppUserId, Guid briefId)
    {
        await using AsyncServiceScope scope = fixture.CreateScope();
        scope.ServiceProvider.GetRequiredService<TestCurrentUser>().UserId = clientAppUserId;
        return await scope.ServiceProvider
            .GetRequiredService<IQueryHandler<Crm.Contracts.Portal.GetMyPortalBriefRoundsQuery, IReadOnlyList<CrmBriefRoundResponse>>>()
            .HandleAsync(new Crm.Contracts.Portal.GetMyPortalBriefRoundsQuery(clientAppUserId, briefId));
    }

    private async Task<Result> UpdateHeadAsync(BriefTestProject p, Guid briefId, CrmBriefHeadData data)
    {
        await using AsyncServiceScope scope = OwnerScope(p);
        return await scope.ServiceProvider.GetRequiredService<ICommandHandler<UpdateCrmBriefHeadCommand>>()
            .HandleAsync(new UpdateCrmBriefHeadCommand(briefId, data, p.OwnerAppUserId));
    }

    private static Task<Result<ApprovalRequestOutcome>> RequestDirectlyAsync(
        IServiceProvider sp, BriefTestProject p, Guid approvalId, Guid briefId) =>
        sp.GetRequiredService<IApprovalRequester>().RequestAsync(
            new ApprovalRequest(
                approvalId, ApprovalSubjectType.DesignBrief, briefId, ApprovalType.ClientReview,
                p.OwnerAppUserId, [new ApproverSpec(ApproverKind.Client, p.ClientAppUserId)], DueAtUtc: null),
            CancellationToken.None);

    /// <summary>Decides as the client through the handler — see the class remarks: not the HTTP route.</summary>
    private async Task<Result> DecideAsClientAsync(
        BriefTestProject p, Guid approvalId, ApprovalDecision decision, string? comment = null)
    {
        await using AsyncServiceScope decideScope = fixture.CreateScope();
        decideScope.ServiceProvider.GetRequiredService<TestCurrentUser>().UserId = p.ClientAppUserId;
        return await decideScope.ServiceProvider.GetRequiredService<ICommandHandler<DecideApprovalCommand>>()
            .HandleAsync(new DecideApprovalCommand(approvalId, decision, comment));
    }

    /// <summary>Twice: Approvals' decided event → CRM's handler, whose own enqueued events are then
    /// delivered on the second pass.</summary>
    private async Task DrainOutboxAsync()
    {
        await fixture.OutboxProcessor.ProcessPendingAsync(CancellationToken.None);
        await fixture.OutboxProcessor.ProcessPendingAsync(CancellationToken.None);
    }

    private async Task<CrmDesignBrief> LoadBriefAsync(Guid briefId)
    {
        await using AsyncServiceScope scope = fixture.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<CrmDbContext>()
            .CrmDesignBriefs.AsNoTracking().SingleAsync(b => b.Id == briefId);
    }

    private async Task<int> CountSnapshotsAsync(Guid briefId)
    {
        await using AsyncServiceScope scope = fixture.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<CrmDbContext>()
            .CrmBriefHeadSnapshots.CountAsync(s => s.BriefId == briefId);
    }

    private async Task<bool> OutboxHasAsync(string eventTypeName, Guid subjectId)
    {
        await using AsyncServiceScope scope = fixture.CreateScope();
        List<string> contents = await scope.ServiceProvider.GetRequiredService<SharedDbContext>()
            .OutboxMessages.AsNoTracking()
            .Where(m => m.Type.Contains(eventTypeName))
            .Select(m => m.Content)
            .ToListAsync();
        return contents.Any(c => c.Contains(subjectId.ToString(), StringComparison.OrdinalIgnoreCase));
    }
}
