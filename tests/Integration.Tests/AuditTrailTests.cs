using System.Linq.Expressions;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using BuildingBlocks.Auditing;
using BuildingBlocks.Outbox;
using Hr.Contracts;
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
        using IServiceScope scope = fixture.Services.CreateScope();

        IAuditContextAccessor accessor = scope.ServiceProvider.GetRequiredService<IAuditContextAccessor>();

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
    ///
    /// <para><b>The HTTP route that creates the user is now <c>POST /api/employees</c>.</b> It used to be
    /// the anonymous <c>/api/auth/register</c>. The move makes this test strictly stronger: that route
    /// carries a plaintext password in its request body AND requires an authenticated caller, so the
    /// actor half of <c>HttpAuditContextAccessor</c> is assertable here too, which it never was
    /// before.</para>
    /// </summary>
    [Fact]
    public async Task CreatingAUser_IsAudited_AndNeverWritesTheirPasswordHashOrSecurityStamp()
    {
        HttpClient client = fixture.CreateClient();
        string suffix = Guid.NewGuid().ToString("N");
        string email = $"audit-secrets-{suffix}@example.com";
        const string password = IdentityTestSeeding.Password;

        string callerId = Guid.NewGuid().ToString();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", fixture.IssueTestToken(callerId, HrPermissions.EmployeesCreate, HrPermissions.EmployeesRead));

        HttpResponseMessage response = await client.PostAsJsonAsync("/api/employees", new
        {
            email,
            password,
            FirstName = "Audit",
            LastName = "Secrets",
            JobTitle = "Engineer",
            HireDate = DateOnly.FromDateTime(DateTime.UtcNow),
            ContractType = "FullTime",
        });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        // The user id belongs to the request, not to the test - read it back off the employee the
        // request created.
        Guid employeeId = (await response.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("employeeId").GetGuid();
        string appUserId = (await client.GetFromJsonAsync<EmployeeSummary>($"/api/employees/{employeeId}"))!.AppUserId;

        AuditLog row = Assert.Single(await ReadAuditRowsAsync(a =>
            a.EntityName == "ApplicationUser" && a.EntityId == appUserId && a.Action == AuditAction.Create));

        Dictionary<string, JsonElement> values = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(row.NewValues!)!;
        Assert.Equal("***", values["PasswordHash"].GetString());
        Assert.Equal("***", values["SecurityStamp"].GetString());
        Assert.Equal("***", values["ConcurrencyStamp"].GetString());

        // Belt and braces against a future property whose name the rules happen not to match: the
        // plaintext password must not appear anywhere in the captured payload either.
        Assert.DoesNotContain(password, row.NewValues);

        // And the row is not vacuously redacted - a non-sensitive value did survive capture.
        Assert.Equal(email.ToUpperInvariant(), values["NormalizedEmail"].GetString());

        // The actor and the request, proving HttpAuditContextAccessor is live rather than merely
        // registered. Unlike the anonymous register route this replaced, the caller is authenticated,
        // so the actor is assertable rather than legitimately null.
        Assert.Equal("identity", row.Module);
        Assert.Equal("/api/employees", row.RequestPath);
        Assert.Equal("POST", row.RequestMethod);
        Assert.Equal(callerId, row.UserId);
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
        HttpClient client = fixture.CreateClient();
        string suffix = Guid.NewGuid().ToString("N");

        // The CALLER is a different user from the subject, so "the row carries the actor" cannot pass
        // by coincidence of the two ids being equal. That is now structural rather than arranged: the
        // person being hired is created BY the request, so they cannot also be making it.
        string callerId = Guid.NewGuid().ToString();
        string token = fixture.IssueTestToken(
            callerId, HrPermissions.EmployeesCreate, HrPermissions.EmployeesRead, HrPermissions.EmployeesSetActive);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        HttpResponseMessage employeeResponse = await client.PostAsJsonAsync("/api/employees", new
        {
            Email = $"audit-outbox-{suffix}@example.com",
            password = IdentityTestSeeding.Password,
            FirstName = "Audit",
            LastName = "Outbox",
            JobTitle = "Engineer",
            HireDate = DateOnly.FromDateTime(DateTime.UtcNow),
            ContractType = "FullTime",
        });
        Assert.Equal(HttpStatusCode.Created, employeeResponse.StatusCode);
        Guid employeeId = (await employeeResponse.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("employeeId").GetGuid();

        // The hire created this user; it is not something the test chose.
        EmployeeSummary employee = (await client.GetFromJsonAsync<EmployeeSummary>($"/api/employees/{employeeId}"))!;
        string appUserId = employee.AppUserId;

        // Resolved through IHostedService, the established pattern in this suite: AddOutbox registers
        // the processor with AddHostedService, so it is not resolvable by its own type.
        OutboxProcessor outboxProcessor = fixture.Services.GetServices<IHostedService>().OfType<OutboxProcessor>().Single();

        // Drain the HIRE first, so its own background write (activation) is behind the snapshot below
        // rather than mixed into the delta this test is measuring.
        await outboxProcessor.ProcessPendingAsync(CancellationToken.None);

        HttpResponseMessage deactivate = await client.PostAsync($"/api/employees/{employeeId}/deactivate", null);
        Assert.Equal(HttpStatusCode.NoContent, deactivate.StatusCode);

        // Half one: the HTTP write, in the hr schema, with the caller on it.
        AuditLog employeeRow = Assert.Single(await ReadAuditRowsAsync(a =>
            a.EntityName == "Employee" && a.EntityId == employeeId.ToString() && a.Action == AuditAction.Update));
        Assert.Equal(callerId, employeeRow.UserId);
        Assert.Equal("hr", employeeRow.Module);
        Assert.Equal($"/api/employees/{employeeId}/deactivate", employeeRow.RequestPath);

        // Snapshot the user's existing audit rows by id, rather than asserting there are none. Creating
        // the user already updates ApplicationUser more than once (role assignment rewrites
        // ConcurrencyStamp among others, and activation flips IsActive), so "no rows yet" is simply
        // false - and a test written against that assumption would be asserting the absence of something
        // unrelated. The delta below is what actually isolates the outbox handler's write.
        var existingRowIds = (await ReadAuditRowsAsync(a =>
            a.EntityName == "ApplicationUser" && a.EntityId == appUserId))
            .Select(r => r.Id)
            .ToHashSet();

        await outboxProcessor.ProcessPendingAsync(CancellationToken.None);

        // Half two: the same logical operation's downstream write, in the identity schema, with NO
        // actor - because the outbox processor's scope has no request behind it.
        var newRows = (await ReadAuditRowsAsync(a =>
            a.EntityName == "ApplicationUser" && a.EntityId == appUserId))
            .Where(r => !existingRowIds.Contains(r.Id))
            .ToList();

        AuditLog userRow = Assert.Single(newRows);
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
        int seedingRows = await CountAuditRowsAsync(a =>
            a.UserId == null
            && (a.EntityName == "ApplicationRole" || a.EntityName == "ApplicationRoleClaim"));

        // Non-vacuous at the bottom (seeding IS audited) and bounded at the top.
        // Measured at N1-02: 232 rows (roles + role claims) on a fresh container.
        Assert.InRange(seedingRows, 1, 2000);
    }

    private async Task<List<AuditLog>> ReadAuditRowsAsync(Expression<Func<AuditLog, bool>> predicate)
    {
        await using AsyncServiceScope scope = fixture.Services.CreateAsyncScope();
        SharedDbContext shared = scope.ServiceProvider.GetRequiredService<SharedDbContext>();
        return await shared.AuditLogs.AsNoTracking().Where(predicate).ToListAsync();
    }

    private async Task<int> CountAuditRowsAsync(Expression<Func<AuditLog, bool>> predicate)
    {
        await using AsyncServiceScope scope = fixture.Services.CreateAsyncScope();
        SharedDbContext shared = scope.ServiceProvider.GetRequiredService<SharedDbContext>();
        return await shared.AuditLogs.AsNoTracking().CountAsync(predicate);
    }
}
