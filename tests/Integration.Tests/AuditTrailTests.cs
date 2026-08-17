using System.Linq.Expressions;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using BuildingBlocks.Auditing;
using BuildingBlocks.Outbox;
using Hr.Contracts;
using Identity.Contracts.Auth;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace Integration.Tests;

/// <summary>
/// N1-02's host-level half. <c>AuditInterceptorTests</c> proves the interceptor's mechanics against a
/// synthetic entity in the project that owns it; this proves the same mechanism is actually wired into
/// the real eight-context host — that <c>AddModuleDbContext</c> attached it, that <c>Api</c>'s HTTP
/// accessor won the registration race against every module's <c>TryAdd</c>'d null one, and that a real
/// user's real write lands with a real actor on it.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class AuditTrailTests(ApiFixture fixture)
{
    /// <summary>
    /// The registration-order contract, asserted rather than commented.
    ///
    /// <para><c>Program.cs</c> runs <c>AddApi</c> before the module loop, so <c>Api</c>'s
    /// <c>HttpAuditContextAccessor</c> is registered first and every module's
    /// <c>TryAddSingleton&lt;IAuditContextAccessor, NullAuditContextAccessor&gt;</c> no-ops. Reverse
    /// those two and nothing breaks, nothing throws, no existing test fails — audit rows simply stop
    /// carrying an actor, permanently. This is the only thing that would notice.</para>
    /// </summary>
    [Fact]
    public void TheRealHost_ResolvesTheHttpAuditContextAccessor_NotTheNullFallback()
    {
        using var scope = fixture.Services.CreateScope();

        var accessor = scope.ServiceProvider.GetRequiredService<IAuditContextAccessor>();

        Assert.Equal("HttpAuditContextAccessor", accessor.GetType().Name);
    }

    /// <summary>
    /// <b>The test that catches the real security bug.</b> <c>ApplicationUser</c> implements
    /// <c>IAuditable</c>, so every registration writes an audit row — and its <c>PasswordHash</c>,
    /// <c>SecurityStamp</c> and <c>ConcurrencyStamp</c> are declared on Microsoft's
    /// <c>IdentityUser&lt;string&gt;</c>, so they cannot carry <c>[AuditIgnore]</c>. Only
    /// <c>AuditRedaction</c>'s NAME rules stand between switching the interceptor on and writing
    /// password hashes into a queryable table on the very first registration.
    ///
    /// <para><c>AuditInterceptorTests</c> asserts the same rules against a synthetic property. This one
    /// asserts them against the actual type whose actual inherited members are the actual hazard — and
    /// also that the interceptor really is attached to <c>IdentityModuleDbContext</c>, since a row must
    /// exist before it can be checked for secrets.</para>
    /// </summary>
    [Fact]
    public async Task RegisteringAUser_IsAudited_AndNeverWritesTheirPasswordHashOrSecurityStamp()
    {
        var client = fixture.CreateClient();
        var suffix = Guid.NewGuid().ToString("N");
        var email = $"audit-secrets-{suffix}@example.com";

        var response = await client.PostAsJsonAsync("/api/auth/register", new RegisterRequest(
            email, "P@ssw0rd123!", "Audit", "Secrets", null, "employee"));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var appUserId = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("userId").GetString()!;

        var row = Assert.Single(await ReadAuditRowsAsync(a =>
            a.EntityName == "ApplicationUser" && a.EntityId == appUserId && a.Action == AuditAction.Create));

        var values = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(row.NewValues!)!;
        Assert.Equal("***", values["PasswordHash"].GetString());
        Assert.Equal("***", values["SecurityStamp"].GetString());
        Assert.Equal("***", values["ConcurrencyStamp"].GetString());

        // Belt and braces against a future property whose name the rules happen not to match: the
        // plaintext password must not appear anywhere in the captured payload either.
        Assert.DoesNotContain("P@ssw0rd123!", row.NewValues);

        // And the row is not vacuously redacted - a non-sensitive value did survive capture.
        Assert.Equal(email.ToUpperInvariant(), values["NormalizedEmail"].GetString());

        // The actor and the request, proving HttpAuditContextAccessor is live rather than merely
        // registered. /api/auth/register is anonymous, so there is no UserId to assert here - the
        // request half is what this endpoint can show, and the actor half is asserted below.
        Assert.Equal("identity", row.Module);
        Assert.Equal("/api/auth/register", row.RequestPath);
        Assert.Equal("POST", row.RequestMethod);
    }

    /// <summary>
    /// <b>One logical operation, two audit rows, and only one of them has an actor.</b>
    ///
    /// <para>Deactivating an employee is an authenticated HTTP write in HR (so its row carries the
    /// caller), which publishes <c>EmployeeDeactivatedIntegrationEvent</c>; the Identity handler that
    /// consumes it runs inside <c>OutboxProcessor</c>'s own scope, where there is no
    /// <c>HttpContext</c> at all (so its row carries none). Asserting both halves in one test is what
    /// makes the null meaningful: it shows the null actor is genuinely "no caller in this scope" rather
    /// than "the accessor never works", which a test of the background path alone could not
    /// distinguish.</para>
    ///
    /// <para>The background row is written, not skipped. Three real paths have no ambient caller —
    /// seeding, the outbox processor, and the periodic job worker — and refusing to audit them would
    /// put the hole exactly where the trail matters most.</para>
    /// </summary>
    [Fact]
    public async Task AWriteThroughTheOutboxBackgroundScope_IsAudited_WithANullActor_UnlikeItsHttpTrigger()
    {
        var client = fixture.CreateClient();
        var suffix = Guid.NewGuid().ToString("N");

        var registerResponse = await client.PostAsJsonAsync("/api/auth/register", new RegisterRequest(
            $"audit-outbox-{suffix}@example.com", "P@ssw0rd123!", "Audit", "Outbox", null, "employee"));
        Assert.Equal(HttpStatusCode.OK, registerResponse.StatusCode);
        var appUserId = (await registerResponse.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("userId").GetString()!;

        // The CALLER is a different user from the subject, so "the row carries the actor" cannot pass
        // by coincidence of the two ids being equal.
        var callerId = Guid.NewGuid().ToString();
        var token = fixture.IssueTestToken(
            callerId, HrPermissions.EmployeesCreate, HrPermissions.EmployeesSetActive);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var employeeResponse = await client.PostAsJsonAsync("/api/employees", new
        {
            AppUserId = appUserId,
            JobTitle = "Engineer",
            HireDate = DateOnly.FromDateTime(DateTime.UtcNow),
            ContractType = "FullTime",
        });
        Assert.Equal(HttpStatusCode.OK, employeeResponse.StatusCode);
        var employeeId = (await employeeResponse.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("employeeId").GetGuid();

        var deactivate = await client.PostAsync($"/api/employees/{employeeId}/deactivate", null);
        Assert.Equal(HttpStatusCode.NoContent, deactivate.StatusCode);

        // Half one: the HTTP write, in the hr schema, with the caller on it.
        var employeeRow = Assert.Single(await ReadAuditRowsAsync(a =>
            a.EntityName == "Employee" && a.EntityId == employeeId.ToString() && a.Action == AuditAction.Update));
        Assert.Equal(callerId, employeeRow.UserId);
        Assert.Equal("hr", employeeRow.Module);
        Assert.Equal($"/api/employees/{employeeId}/deactivate", employeeRow.RequestPath);

        // Snapshot the user's existing audit rows by id, rather than asserting there are none.
        // Registration itself already updates ApplicationUser once (role assignment rewrites
        // ConcurrencyStamp among others), so "no rows yet" is simply false - and a test written against
        // that assumption would be asserting the absence of something unrelated. The delta below is
        // what actually isolates the outbox handler's write.
        var existingRowIds = (await ReadAuditRowsAsync(a =>
            a.EntityName == "ApplicationUser" && a.EntityId == appUserId))
            .Select(r => r.Id)
            .ToHashSet();

        // Resolved through IHostedService, the established pattern in this suite: AddOutbox registers
        // the processor with AddHostedService, so it is not resolvable by its own type.
        var outboxProcessor = fixture.Services.GetServices<IHostedService>().OfType<OutboxProcessor>().Single();
        await outboxProcessor.ProcessPendingAsync(CancellationToken.None);

        // Half two: the same logical operation's downstream write, in the identity schema, with NO
        // actor - because the outbox processor's scope has no request behind it.
        var newRows = (await ReadAuditRowsAsync(a =>
            a.EntityName == "ApplicationUser" && a.EntityId == appUserId))
            .Where(r => !existingRowIds.Contains(r.Id))
            .ToList();

        var userRow = Assert.Single(newRows);
        Assert.Equal(AuditAction.Update, userRow.Action);
        Assert.Null(userRow.UserId);
        Assert.Null(userRow.RequestPath);
        Assert.Null(userRow.RequestMethod);

        // What and when survive, which is the entire reason the row is written at all.
        Assert.Equal("identity", userRow.Module);
        Assert.NotEqual(default, userRow.Timestamp);
    }

    /// <summary>
    /// <b>A measurement, not a correctness assertion</b> — recorded so that deferred audit retention is
    /// a sized decision rather than a theoretical one. ~56 entity types implement <c>IAuditable</c>, and
    /// Identity's cold-boot seeding writes a role row per role and a claim row per permission; every
    /// fresh container pays this, including every CI run.
    ///
    /// <para>The bound is deliberately loose. It exists to catch an order-of-magnitude change — a
    /// seeder that stopped being idempotent, or something enormous becoming auditable — not to pin a
    /// number that legitimately moves whenever a permission is added. The exact figure for this build
    /// is recorded in <c>PROGRESS.md</c>; the test's job is to notice when it stops being that shape.</para>
    /// </summary>
    [Fact]
    public async Task ColdBootSeeding_WritesAuditRows_InTheExpectedOrderOfMagnitude()
    {
        var seedingRows = await CountAuditRowsAsync(a =>
            a.UserId == null
            && (a.EntityName == "ApplicationRole" || a.EntityName == "ApplicationRoleClaim"));

        // Non-vacuous at the bottom (seeding IS audited) and bounded at the top.
        // Measured at N1-02: 232 rows (roles + role claims) on a fresh container.
        Assert.InRange(seedingRows, 1, 2000);
    }

    private async Task<List<AuditLog>> ReadAuditRowsAsync(Expression<Func<AuditLog, bool>> predicate)
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var shared = scope.ServiceProvider.GetRequiredService<SharedDbContext>();
        return await shared.AuditLogs.AsNoTracking().Where(predicate).ToListAsync();
    }

    private async Task<int> CountAuditRowsAsync(Expression<Func<AuditLog, bool>> predicate)
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var shared = scope.ServiceProvider.GetRequiredService<SharedDbContext>();
        return await shared.AuditLogs.AsNoTracking().CountAsync(predicate);
    }
}
