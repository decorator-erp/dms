using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Notifications.Contracts;
using Xunit;

namespace Integration.Tests;

/// <summary>
/// N1-08: the eighth module's schema isolation, in the shape
/// <c>{Crm,Projects,Documents}SchemaIsolationTests</c> established — EF query logging captured through
/// <c>ApiFixture.CapturedSql</c>, cleared immediately before the operation under test so earlier noise
/// cannot produce a false pass.
///
/// <para><b>Notifications is the module where this matters most, and for an unusual reason.</b> It is
/// the one module that legitimately reads a table it does not own — <c>shared.audit_logs</c>, mapped
/// with <c>ExcludeFromMigrations</c> — so "touches only its own schema" is false by construction and
/// the useful assertion is narrower: the inbox read touches <c>notify</c> and nothing belonging to
/// another MODULE. <c>shared</c> is excluded from the filter deliberately, and the audit read below
/// asserts the opposite direction so the exclusion cannot hide a mistake.</para>
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class NotificationsSchemaIsolationTests(ApiFixture fixture)
{
    private static readonly string[] OtherModuleSchemas =
        ["identity.", "hr.", "approvals.", "crm.", "projects.", "documents.", "sites."];

    [Fact]
    public async Task ReadingTheInbox_IssuesNoSqlAgainstAnotherModulesSchema()
    {
        string userId = Guid.NewGuid().ToString();
        HttpClient client = fixture.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", fixture.IssueTestToken(userId, NotificationsPermissions.ViewOwn));

        // Warm the route once so first-request model building and JIT are not what gets captured.
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/notifications")).StatusCode);

        fixture.ClearCapturedSql();

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/notifications")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/notifications/unread-count")).StatusCode);

        // Stronger than the helper's own liveness check: the inbox read must have touched notify.,
        // which proves the measured window covers these two requests rather than merely being non-empty.
        Assert.Contains(fixture.CapturedSql, sql => sql.Contains("notify.", StringComparison.OrdinalIgnoreCase));
        AssertNoSqlTouching(OtherModuleSchemas);
    }

    /// <summary>
    /// The audit read is the exception, and it is asserted as one rather than excluded from the rule.
    /// <c>/api/audit</c> MUST touch <c>shared</c> — that is where <c>audit_logs</c> lives — and must
    /// still touch no other module's schema. Asserting the positive half is what stops the negative
    /// half above from passing because the endpoint quietly stopped querying anything.
    /// </summary>
    [Fact]
    public async Task ReadingTheAuditLog_TouchesSharedAndNoOtherModulesSchema()
    {
        HttpClient client = fixture.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", fixture.IssueTestToken(Guid.NewGuid().ToString(), NotificationsPermissions.AuditRead));

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/audit")).StatusCode);

        fixture.ClearCapturedSql();
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/audit")).StatusCode);

        Assert.Contains(fixture.CapturedSql, sql => sql.Contains("shared.", StringComparison.OrdinalIgnoreCase));
        AssertNoSqlTouching(OtherModuleSchemas);
    }

    /// <summary>
    /// Writing a notification is the same story from the other side: <c>NotificationService</c> resolves
    /// its recipient from an id it was handed and never looks one up itself, so the write path touches
    /// <c>notify</c> only. This is what keeps the module a hub rather than something that reaches into
    /// seven schemas to decide who to tell — the handlers that DO need another module's tables were
    /// moved into those modules at N1-06.
    /// </summary>
    [Fact]
    public async Task WritingANotification_IssuesNoSqlAgainstAnotherModulesSchema()
    {
        // A real user, because notify.notifications has a foreign key to identity.users.
        string recipient = await RegisterUserAsync();

        await using AsyncServiceScope scope = fixture.Services.CreateAsyncScope();
        INotificationService notifications = scope.ServiceProvider.GetRequiredService<INotificationService>();

        fixture.ClearCapturedSql();

        await notifications.NotifyAsync(
            recipient, NotificationType.TaskAssigned, "Isolation probe", ct: CancellationToken.None);

        // The write must have reached notify.notifications - see the inbox test for why the helper's
        // NotEmpty alone is not the whole control.
        Assert.Contains(fixture.CapturedSql, sql => sql.Contains("notify.", StringComparison.OrdinalIgnoreCase));
        AssertNoSqlTouching(OtherModuleSchemas);
    }

    /// <summary>
    /// PC-02: the liveness half lives here so all three callers inherit it. N1-08 found
    /// <c>CapturedSql</c> had never captured anything — every assertion below passed against an empty
    /// list for three phases. An empty capture now fails here rather than reading as a clean result.
    /// </summary>
    private void AssertNoSqlTouching(string[] forbiddenSchemas)
    {
        Assert.NotEmpty(fixture.CapturedSql);

        var offending = fixture.CapturedSql
            .Where(sql => forbiddenSchemas.Any(s => sql.Contains(s, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        Assert.True(offending.Count == 0,
            $"Notifications issued SQL touching another module's schema: {string.Join(" | ", offending)}");
    }

    private Task<string> RegisterUserAsync() =>
        IdentityTestSeeding.ProvisionActiveUserAsync(
            fixture, $"iso-{Guid.NewGuid():N}@example.com", firstName: "Iso");
}
