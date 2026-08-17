using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Approvals.Contracts;
using BuildingBlocks.Jobs;
using Identity.Contracts.Auth;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Npgsql;
using Xunit;

namespace Integration.Tests;

/// <summary>
/// N1-05 verify: the job inversion, through the real host.
///
/// <para>The backend runs nine <c>BackgroundService</c> loops, each scanning another area's tables on
/// its own schedule — the single biggest avoidable always-on cost in that design. Here seven jobs live
/// in the three modules that own their data and one <c>PeriodicJobWorker</c> runs them all on one
/// timer. These tests cover the two properties that makes possible: every job is registered (a job that
/// silently never runs is the failure mode this repo has hit three times), and every job is idempotent
/// (which is what lets one timer re-run all seven without duplicating anything).</para>
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class PeriodicJobTests(ApiFixture fixture)
{
    /// <summary>
    /// <b>All seven, by name.</b> Registration is per module via a Scrutor scan, and a scan that finds
    /// nothing throws nothing — G5-07 was exactly this (wrong scanned assembly, silent zero
    /// decoration), and the symptom of a missing job here would be "some reminders stopped", months
    /// later, with nothing pointing at the cause.
    ///
    /// <para><b>Seven, not eight.</b> <c>InvoiceDueJob</c> reads <c>Invoices</c> and ships with Finance.
    /// Asserting the exact set rather than a count is what makes that a recorded boundary rather than an
    /// omission — swapping one job for another would keep a count green.</para>
    /// </summary>
    [Fact]
    public void EverySevenPeriodicJobs_AreRegistered_AcrossTheirOwningModules()
    {
        using var scope = fixture.Services.CreateScope();

        var jobNames = scope.ServiceProvider.GetServices<IPeriodicJob>()
            .Select(job => job.Name)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();

        Assert.Equal(
            [
                "ApprovalOverdueEscalationJob",  // Approvals
                "ApprovalResponseDueJob",        // Approvals
                "CrmMeetingReminderJob",         // Crm
                "CrmPeriodCloseJob",             // Crm
                "ProjectDeadlineJob",            // Projects
                "StageDeadlineJob",              // Projects
                "TaskDeadlineJob",               // Projects
            ],
            jobNames);
    }

    /// <summary>
    /// One tick runs every job. Drives the real worker resolved from the host — not the jobs
    /// individually — so this also covers the worker's own resolve-by-name path and its per-job scope
    /// and try/catch.
    /// </summary>
    [Fact]
    public async Task PeriodicJobWorker_ExecutesEveryJob_InOneTick()
    {
        var worker = fixture.Services.GetServices<IHostedService>().OfType<PeriodicJobWorker>().Single();

        await worker.RunAllJobsAsync(CancellationToken.None);
    }

    /// <summary>
    /// <b>The property the whole consolidated-sweep design rests on.</b> An overdue approval notifies
    /// its approvers; a second tick notifies nobody again.
    ///
    /// <para>Asserted per <c>(dedupe key, recipient)</c> pair rather than as a bare count: a count
    /// assertion would also pass if the second run had written a row for a DIFFERENT recipient, which
    /// is precisely the failure that a mis-scoped dedupe key produces.</para>
    ///
    /// <para>Two approvers on one approval, so the fan-out and the idempotency are proved together —
    /// with a per-recipient key, two rows on the first tick and none on the second; with an unscoped
    /// key, one row and one silently dropped.</para>
    /// </summary>
    [Fact]
    public async Task AnOverdueApproval_NotifiesEveryApproverOnce_AndNotAgainOnASecondTick()
    {
        var firstApprover = await RegisterUserAsync();
        var secondApprover = await RegisterUserAsync();
        var (approvalId, requesterId) = await RequestOverdueApprovalAsync(firstApprover, secondApprover);

        var worker = fixture.Services.GetServices<IHostedService>().OfType<PeriodicJobWorker>().Single();

        await worker.RunAllJobsAsync(CancellationToken.None);
        var afterFirstTick = await ReadDedupeKeysAsync(approvalId);

        await worker.RunAllJobsAsync(CancellationToken.None);
        var afterSecondTick = await ReadDedupeKeysAsync(approvalId);

        // Thirty days overdue exercises BOTH Approvals jobs from one seeded row: the SLA nudge to each
        // eligible approver, and the escalation to the requester past the two-day grace window. Their
        // dedupe keys are deliberately distinct (`-sla` vs `-escalated`) so neither suppresses the
        // other — asserting all three together is what proves that.
        string[] expected =
        [
            $"approval-{approvalId}-escalated:{requesterId}",
            $"approval-{approvalId}-sla:{firstApprover}",
            $"approval-{approvalId}-sla:{secondApprover}",
        ];

        Assert.Equal(expected.Order(StringComparer.Ordinal), afterFirstTick.Order(StringComparer.Ordinal));
        Assert.Equal(afterFirstTick.Order(StringComparer.Ordinal), afterSecondTick.Order(StringComparer.Ordinal));
    }

    /// <summary>
    /// <b>Approvals' two jobs read the approvals schema and nothing else.</b>
    ///
    /// <para>This is the strongest schema-isolation claim in the phase and it is specific to this
    /// module: G4-01's redesign stores approvers as Identity user ids resolved by the REQUESTING module,
    /// so these jobs need no employee or client lookup — no <c>IHrDirectory</c>, no
    /// <c>IClientDirectory</c>, no cross-module contract at all. The backend's equivalents resolve an
    /// employee-or-client per row and escalate through <c>Approval.ProjectId</c>. If a future change
    /// reintroduces either, this goes red.</para>
    ///
    /// <para><c>notify</c> and <c>shared</c> are expected and excluded from the filter: writing a
    /// notification and staging an audit row are what the job is FOR.</para>
    /// </summary>
    [Fact]
    public async Task ApprovalsJobs_IssueNoSqlAgainstAnotherModulesSchema()
    {
        await RequestOverdueApprovalAsync(await RegisterUserAsync());

        var approvalsJobs = ResolveJobs("ApprovalResponseDueJob", "ApprovalOverdueEscalationJob");

        fixture.ClearCapturedSql();
        foreach (var job in approvalsJobs)
            await job.RunAsync(CancellationToken.None);

        AssertNoSqlTouching(["identity.", "hr.", "crm.", "projects.", "documents.", "sites."]);
    }

    /// <summary>
    /// Projects' and CRM's jobs legitimately reach other modules — a project's owner is an HR employee,
    /// its client a CRM client — but only through published contracts, and only to modules they already
    /// depend on. Two schemas they have no contract for and no business in at all:
    /// <c>documents</c> and <c>sites</c>.
    ///
    /// <para>A weaker assertion than the Approvals one above, deliberately. Asserting "touches no other
    /// schema" here would be false by design, and writing a test that has to be true would be worse
    /// than writing none.</para>
    /// </summary>
    [Fact]
    public async Task ProjectsAndCrmJobs_NeverTouchTheDocumentsOrSitesSchemas()
    {
        var jobs = ResolveJobs(
            "ProjectDeadlineJob", "StageDeadlineJob", "TaskDeadlineJob",
            "CrmMeetingReminderJob", "CrmPeriodCloseJob");

        fixture.ClearCapturedSql();
        foreach (var job in jobs)
            await job.RunAsync(CancellationToken.None);

        AssertNoSqlTouching(["documents.", "sites."]);
    }

    private List<IPeriodicJob> ResolveJobs(params string[] names)
    {
        var scope = fixture.Services.CreateScope();
        var jobs = scope.ServiceProvider.GetServices<IPeriodicJob>()
            .Where(job => names.Contains(job.Name, StringComparer.Ordinal))
            .ToList();

        Assert.Equal(names.Length, jobs.Count);
        return jobs;
    }

    /// <summary>
    /// PC-02: liveness for both callers. A job that issued no SQL at all — because it was never
    /// resolved, or returned early — satisfies "touched no forbidden schema" without running. This is
    /// the <c>CapturedSql</c> failure N1-08 found, applied to the job path.
    /// </summary>
    private void AssertNoSqlTouching(string[] forbiddenSchemas)
    {
        Assert.NotEmpty(fixture.CapturedSql);

        var offending = fixture.CapturedSql
            .Where(sql => forbiddenSchemas.Any(s => sql.Contains(s, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        Assert.True(offending.Count == 0,
            $"A periodic job issued SQL touching {string.Join("/", forbiddenSchemas)}: "
            + string.Join(" | ", offending));
    }

    /// <summary>
    /// Registers a real Identity user. Not optional decoration: <c>approvals.approvals</c> has a
    /// foreign key on <c>RequestedByUserId</c> and <c>notify.notifications</c> one on
    /// <c>RecipientUserId</c>, both to <c>identity.users</c>, so a synthetic Guid fails the insert.
    /// Those constraints working is itself part of what N1-03 shipped.
    /// </summary>
    private async Task<string> RegisterUserAsync()
    {
        var client = fixture.CreateClient();
        var response = await client.PostAsJsonAsync("/api/auth/register", new RegisterRequest(
            $"job-{Guid.NewGuid():N}@example.com", "P@ssw0rd123!", "Job", "User", null, "employee"));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("userId").GetString()!;
    }

    /// <summary>Creates an approval already past its response deadline, through the public
    /// <see cref="IApprovalRequester"/> contract — the same way Projects creates one.</summary>
    private async Task<(Guid ApprovalId, string RequesterId)> RequestOverdueApprovalAsync(params string[] approverUserIds)
    {
        var requestedByUserId = await RegisterUserAsync();

        await using var scope = fixture.Services.CreateAsyncScope();
        var requester = scope.ServiceProvider.GetRequiredService<IApprovalRequester>();

        var result = await requester.RequestAsync(
            new ApprovalRequest(
                ApprovalSubjectType.GateCondition,
                Guid.NewGuid(),
                ApprovalType.ManagerApproval,
                RequestedByUserId: requestedByUserId,
                Approvers: [.. approverUserIds.Select(id => new ApproverSpec(ApproverKind.Employee, id))],
                // Well past both the SLA and the escalation grace window, so one seeded row exercises
                // both jobs.
                DueAtUtc: DateTimeOffset.UtcNow.AddDays(-30)),
            CancellationToken.None);

        Assert.True(result.IsSuccess, $"Seeding an approval failed: {result.Error?.Description}");
        return (result.Value, requestedByUserId);
    }

    /// <summary>Read straight from Postgres: the notification read endpoints do not exist until N1-07,
    /// and the stored dedupe key is exactly what these tests are about.</summary>
    private async Task<List<string>> ReadDedupeKeysAsync(Guid approvalId)
    {
        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();

        await using var command = new NpgsqlCommand(
            """SELECT "DedupeKey" FROM notify.notifications WHERE "EntityId" = @id AND "DedupeKey" IS NOT NULL""",
            connection);
        command.Parameters.AddWithValue("id", approvalId);

        var keys = new List<string>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            keys.Add(reader.GetString(0));

        return keys;
    }
}
