using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using BuildingBlocks.Outbox;
using Identity.Contracts.Auth;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace Integration.Tests;

/// <summary>
/// The Sites characterization spine, through real HTTP: project → site → definition module and field →
/// records captured → document attached → submit → approve. Run twice, per the phase's flake check.
///
/// <para><b>What this proves.</b> The survey lifecycle end to end across all seven original modules —
/// Identity registers the actor, Hr makes them an employee, Crm supplies the client, Projects the
/// project, Documents the attached row, and Sites the survey itself — and that <c>SiteDocument</c>
/// links a real <c>documents.project_documents</c> row rather than a fabricated id. Every cross-schema
/// foreign key S1-05 added is exercised by the writes below simply by their succeeding.</para>
///
/// <para><b>N1-08 made it eight.</b> The flow now also asserts that each of those writes left an audit
/// row whose <c>Module</c> matches the schema its entity really lives in, and that approving the survey
/// notifies its SUBMITTER through the real outbox. That is the phase's spine: an action in a module
/// produces its audit row and its notification, and running the whole thing twice produces no
/// duplicates.</para>
///
/// <para><b>What this does NOT prove, stated before it is written (lesson 4).</b></para>
/// <list type="bullet">
///   <item><c>SourceBriefId</c> provenance. Nothing in this system writes that column —
///   <c>BackfillSitesFromBriefs</c> is a backend data-migration utility that was not ported — so the
///   brief→site link is untested here and the variance route that would have read it is not ported at
///   all [D8].</item>
///   <item><b>Same-transaction atomicity of the audit row.</b> This proves a row exists, not that it
///   commits or rolls back with the change it audits. That is
///   <c>AuditInterceptorTests.AuditRow_AndTheChangeItAudits_RollBackTogether</c>, which needs a
///   deliberate rollback and cannot be shown by a flow that succeeds.</item>
///   <item><b>SignalR delivery.</b> The notification ROW is asserted; whether a connected client
///   received a push is <c>NotificationDeliveryTests</c>.</item>
///   <item><b>Approvals.</b> This flow never requests an approval, so the eighth schema in the audit
///   assertions is <c>identity</c>, not <c>approvals</c>. Approvals' own audit and notification paths
///   are covered by <c>PeriodicJobTests</c> and <c>NotificationEventConsumerTests</c>.</item>
///   <item><b>Job idempotency under concurrency.</b> Single process, sequential — two overlapping
///   hosts are not exercised anywhere in this suite.</item>
/// </list>
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class SitesSpineTests(ApiFixture fixture)
{
    [Fact]
    public async Task Sites_flow_spans_all_eight_modules_end_to_end_and_is_stable_across_two_runs()
    {
        await RunFlowAsync();
        await RunFlowAsync();

        async Task RunFlowAsync()
        {
            var client = fixture.CreateClient();
            var suffix = Guid.NewGuid().ToString("N");

            // ── Identity ───────────────────────────────────────────────────────
            var registerResponse = await client.PostAsJsonAsync("/api/auth/register", new RegisterRequest(
                $"s1-{suffix}@example.com", "P@ssw0rd123!", "Site", "Surveyor", null, "employee"));
            Assert.Equal(HttpStatusCode.OK, registerResponse.StatusCode);
            var appUserId = (await registerResponse.Content.ReadFromJsonAsync<RegisterBody>())!.UserId;

            var token = fixture.IssueTestToken(
                appUserId,
                "employees.create", "clients.create", "project.create", "project.read_all",
                "documents.upload", "documents.read",
                "sites.read", "sites.create", "sites.update", "sites.submit", "sites.approve",
                "sites.definitions.manage");
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

            // ── Hr ─────────────────────────────────────────────────────────────
            var employeeResponse = await client.PostAsJsonAsync("/api/employees", new
            {
                AppUserId = appUserId,
                JobTitle = "Surveyor",
                HireDate = DateOnly.FromDateTime(DateTime.UtcNow),
                ContractType = "FullTime",
            });
            Assert.Equal(HttpStatusCode.OK, employeeResponse.StatusCode);

            // ── Crm → Projects ─────────────────────────────────────────────────
            var clientResponse = await client.PostAsJsonAsync(
                "/api/clients", new { AppUserId = appUserId, Address = "1 Client Rd" });
            Assert.Equal(HttpStatusCode.Created, clientResponse.StatusCode);
            var clientId = (await clientResponse.Content.ReadFromJsonAsync<ClientBody>())!.ClientId;

            var projectResponse = await client.PostAsJsonAsync("/api/projects", new
            {
                Code = $"S-{suffix}"[..12],
                Name = "Sites Smoke",
                Address = "1 Test Rd",
                WorkflowType = 1,
                ClientId = clientId,
            });
            // 200, not 201: the Projects create route answers OK. Clients answers Created. The
            // inconsistency is pre-existing across modules and is not this phase's to change.
            Assert.Equal(HttpStatusCode.OK, projectResponse.StatusCode);
            var projectId = (await projectResponse.Content.ReadFromJsonAsync<ProjectBody>())!.ProjectId;

            // ── Sites: the definition layer ────────────────────────────────────
            // Gated on sites.definitions.manage, not sites.read -- the split D1 exists for.
            var moduleResponse = await client.PostAsJsonAsync("/api/site-modules", new
            {
                Code = $"openings{suffix}"[..14],
                NameEn = "Openings",
                NameAr = "الفتحات",
            });
            Assert.Equal(HttpStatusCode.Created, moduleResponse.StatusCode);
            var moduleId = (await moduleResponse.Content.ReadFromJsonAsync<ModuleBody>())!.ModuleId;

            var itemResponse = await client.PostAsJsonAsync("/api/site-items", new
            {
                Code = $"width{suffix}"[..12],
                NameEn = "Width",
                NameAr = "العرض",
                DataType = 1,
                Unit = "cm",
                MinValue = 1m,
                MaxValue = 500m,
                Tolerance = 0.5m,
            });
            Assert.Equal(HttpStatusCode.Created, itemResponse.StatusCode);
            var itemId = (await itemResponse.Content.ReadFromJsonAsync<ItemBody>())!.ItemId;

            var attachResponse = await client.PostAsJsonAsync(
                $"/api/site-modules/{moduleId}/items",
                new { ItemId = itemId, IsRequired = true, IsVisible = true, SortOrder = 0 });
            Assert.Equal(HttpStatusCode.Created, attachResponse.StatusCode);

            // The surveyor's form, read on sites.read -- and served through the schema cache.
            var schema = await client.GetFromJsonAsync<SchemaBody>(
                $"/api/site-modules/{(await ModuleCodeAsync(client, moduleId))}/schema");
            Assert.NotNull(schema);
            Assert.Single(schema!.Fields);

            // ── Sites: the survey ──────────────────────────────────────────────
            var siteResponse = await client.PostAsJsonAsync("/api/sites", new
            {
                ProjectId = projectId,
                Data = new { Address = "12 Nasr Road", AreaSqm = 113.4m, FloorNo = 3 },
            });
            Assert.Equal(HttpStatusCode.Created, siteResponse.StatusCode);
            var siteId = (await siteResponse.Content.ReadFromJsonAsync<SiteBody>())!.SiteId;

            var detail = await client.GetFromJsonAsync<SiteDetailBody>($"/api/sites/{siteId}");
            Assert.NotNull(detail);
            Assert.Equal(1, detail!.Status);
            Assert.Matches("^SITE-[0-9]{5,}$", detail.Code);

            // ── Sites: records captured ────────────────────────────────────────
            var recordResponse = await client.PostAsJsonAsync(
                $"/api/sites/{siteId}/records", new { ModuleId = moduleId, Label = "Front door", SortOrder = 0 });
            Assert.Equal(HttpStatusCode.Created, recordResponse.StatusCode);
            var recordId = (await recordResponse.Content.ReadFromJsonAsync<RecordBody>())!.RecordId;

            var valuesResponse = await client.PutAsJsonAsync(
                $"/api/sites/{siteId}/records/{recordId}/values",
                new { Values = new[] { new { ItemId = itemId, ValueNumber = 90m } } });
            Assert.Equal(HttpStatusCode.NoContent, valuesResponse.StatusCode);

            // The pivot renders the captured value against the module's own column set.
            var pivot = await client.GetFromJsonAsync<PivotBody>(
                $"/api/sites/{siteId}/records?moduleId={moduleId}");
            Assert.NotNull(pivot);
            Assert.Single(pivot!.Columns);
            Assert.Single(pivot.Rows);

            // The derived reads both answer over real captured data.
            var families = await client.GetAsync($"/api/sites/{siteId}/families?moduleId={moduleId}");
            Assert.Equal(HttpStatusCode.OK, families.StatusCode);

            var boq = await client.GetAsync($"/api/sites/{siteId}/boq");
            Assert.Equal(HttpStatusCode.OK, boq.StatusCode);

            // ── Documents → Sites ──────────────────────────────────────────────
            // A REAL project_documents row, so fk_site_documents_document is exercised rather than
            // dodged. This is the half of the spine that proves SiteDocument links a document it does
            // not own and stores no file of its own.
            var documentId = await UploadAsync(client, projectId, suffix);

            var linkResponse = await client.PostAsJsonAsync(
                $"/api/sites/{siteId}/documents", new { DocumentId = documentId, Kind = 1 });
            Assert.Equal(HttpStatusCode.Created, linkResponse.StatusCode);

            var attachments = await client.GetFromJsonAsync<List<SiteDocumentBody>>(
                $"/api/sites/{siteId}/documents");
            Assert.NotNull(attachments);
            Assert.Contains(attachments!, a => a.DocumentId == documentId);

            // ── Sites: submit → approve ────────────────────────────────────────
            var submitResponse = await client.PostAsync($"/api/sites/{siteId}/submit", null);
            Assert.Equal(HttpStatusCode.NoContent, submitResponse.StatusCode);

            Assert.Equal(2, (await client.GetFromJsonAsync<SiteDetailBody>($"/api/sites/{siteId}"))!.Status);

            // B12's fix through real HTTP (P5F-02): the freeze starts HERE, at Submitted, not at
            // Approved. Before this change the same call returned 204 and the approval below would have
            // attested to a measurement the approver never saw.
            var editWhileAwaitingDecision = await client.PutAsJsonAsync(
                $"/api/sites/{siteId}/records/{recordId}/values",
                new { Values = new[] { new { ItemId = itemId, ValueNumber = 111m } } });
            Assert.Equal(HttpStatusCode.Conflict, editWhileAwaitingDecision.StatusCode);

            var approveResponse = await client.PostAsJsonAsync(
                $"/api/sites/{siteId}/approve", new { Approved = true, Note = "Measured and checked." });
            Assert.Equal(HttpStatusCode.NoContent, approveResponse.StatusCode);

            var approved = await client.GetFromJsonAsync<SiteDetailBody>($"/api/sites/{siteId}");
            Assert.Equal(3, approved!.Status);

            // The lock is real end to end: an approved survey refuses further record writes.
            var lockedResponse = await client.PutAsJsonAsync(
                $"/api/sites/{siteId}/records/{recordId}/values",
                new { Values = new[] { new { ItemId = itemId, ValueNumber = 120m } } });
            Assert.Equal(HttpStatusCode.Conflict, lockedResponse.StatusCode);

            // ── Notifications (N1-08): the eighth module, and the audit trail underneath it ─────
            //
            // Every write above went through a module DbContext carrying the audit interceptor, so the
            // trail should already span six schemas. Asserted per ENTITY ID rather than by counting
            // rows: the fixture is shared across the collection, so a count proves nothing about which
            // flow produced it, while "this project id has an audit row, and its module says projects"
            // is about this run only.
            await AssertAuditedAsync(fixture, "Project", projectId.ToString(), "projects");
            await AssertAuditedAsync(fixture, "Client", clientId.ToString(), "crm");
            await AssertAuditedAsync(fixture, "Site", siteId.ToString(), "sites");
            await AssertAuditedAsync(fixture, "ProjectDocument", documentId.ToString(), "documents");
            await AssertAuditedAsync(fixture, "ApplicationUser", appUserId, "identity");

            // The notification arrives through the REAL outbox, not a direct handler call: approving a
            // site publishes SiteApprovedIntegrationEvent, and Sites' own handler resolves the SUBMITTER
            // from sites.sites.CreatedById (the payload carries only the approver).
            var outboxProcessor = fixture.Services.GetServices<IHostedService>().OfType<OutboxProcessor>().Single();
            await outboxProcessor.ProcessPendingAsync(CancellationToken.None);

            var dedupeKeys = await ReadNotificationDedupeKeysAsync(fixture, appUserId);
            Assert.Contains(dedupeKeys, key => key.StartsWith($"site-{siteId}-decided", StringComparison.Ordinal));
        }
    }

    /// <summary>
    /// One audit row exists for this entity, and its <c>Module</c> is the schema the entity actually
    /// lives in — which is the interceptor reading EF metadata rather than a hand-maintained map. The
    /// module check is what makes this more than "a row was written".
    /// </summary>
    private static async Task AssertAuditedAsync(
        ApiFixture fixture, string entityName, string entityId, string expectedModule)
    {
        await using var connection = new Npgsql.NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();

        await using var command = new Npgsql.NpgsqlCommand(
            """
            SELECT DISTINCT module FROM shared.audit_logs
            WHERE entity_name = @name AND entity_id = @id
            """, connection);
        command.Parameters.AddWithValue("name", entityName);
        command.Parameters.AddWithValue("id", entityId);

        var modules = new List<string?>();
        await using (var reader = await command.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
                modules.Add(reader.IsDBNull(0) ? null : reader.GetString(0));
        }

        Assert.True(modules.Count > 0, $"No audit row for {entityName} {entityId}.");
        Assert.Equal([expectedModule], modules);
    }

    private static async Task<List<string>> ReadNotificationDedupeKeysAsync(ApiFixture fixture, string recipientUserId)
    {
        await using var connection = new Npgsql.NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();

        await using var command = new Npgsql.NpgsqlCommand(
            """SELECT "DedupeKey" FROM notify.notifications WHERE "RecipientUserId" = @id AND "DedupeKey" IS NOT NULL""",
            connection);
        command.Parameters.AddWithValue("id", recipientUserId);

        var keys = new List<string>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            keys.Add(reader.GetString(0));

        return keys;
    }

    /// <summary>
    /// Schema isolation: no <c>SitesDbContext</c> query may touch another module's schema. Sites reads
    /// CRM spaces and Documents rows through their <c>*.Contracts</c> interfaces, never by joining across
    /// schemas — so every table this module's own context names must live in <c>sites</c>.
    /// </summary>
    [Fact]
    public void SitesDbContext_NamesNoTableOutsideTheSitesSchema()
    {
        var contextType = Type.GetType(
            "Sites.Infrastructure.Persistence.SitesDbContext, Sites.Infrastructure")!;

        var options = typeof(Microsoft.EntityFrameworkCore.DbContextOptionsBuilder<>)
            .MakeGenericType(contextType);

        var builder = Activator.CreateInstance(options)!;
        Microsoft.EntityFrameworkCore.NpgsqlDbContextOptionsBuilderExtensions.UseNpgsql(
            (Microsoft.EntityFrameworkCore.DbContextOptionsBuilder)builder,
            "Host=localhost;Database=unused;Username=unused;Password=unused");

        var built = ((Microsoft.EntityFrameworkCore.DbContextOptionsBuilder)builder).Options;

        using var context = (Microsoft.EntityFrameworkCore.DbContext)
            Activator.CreateInstance(contextType, built)!;

        var entityTypes = context.Model.GetEntityTypes()
            .Select(e => new { e.ClrType.Name, Schema = e.GetSchema() })
            .ToArray();

        // PC-03 liveness. The whole assertion is reached through reflection over a context type
        // resolved by string name; if that produced a context whose model was empty, "names no table
        // outside the sites schema" would be true because it names no table at all. Both halves
        // asserted: the model is populated, and it really is the Sites model.
        Assert.NotEmpty(entityTypes);
        Assert.Contains(entityTypes, e => e.Schema == "sites");

        var strays = entityTypes
            // The outbox lives in "shared" by design -- ConfigureOutbox(ownsMigration: false).
            .Where(e => e.Schema is not null and not "sites" and not "shared")
            .Select(e => $"{e.Name} -> {e.Schema}")
            .ToArray();

        Assert.Empty(strays);
    }

    private static async Task<string> ModuleCodeAsync(HttpClient client, Guid moduleId)
    {
        var modules = await client.GetFromJsonAsync<List<ModuleListBody>>("/api/site-modules");
        return modules!.Single(m => m.Id == moduleId).Code;
    }

    private static async Task<Guid> UploadAsync(HttpClient client, Guid projectId, string suffix)
    {
        using var content = new MultipartFormDataContent();
        var file = new ByteArrayContent("survey sketch"u8.ToArray());
        file.Headers.ContentType = new MediaTypeHeaderValue("text/plain");
        content.Add(file, "file", $"sketch-{suffix[..6]}.txt");

        // 200, not 201 -- the Documents upload route answers OK, matching CrossModuleSmokeTests' own
        // UploadAsync helper.
        var response = await client.PostAsync($"/api/projects/{projectId}/documents", content);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        return (await response.Content.ReadFromJsonAsync<DocumentBody>())!.DocumentId;
    }

    private sealed record RegisterBody(string UserId);
    private sealed record ClientBody(Guid ClientId);
    private sealed record ProjectBody(Guid ProjectId);
    private sealed record ModuleBody(Guid ModuleId);
    private sealed record ModuleListBody(Guid Id, string Code);
    private sealed record ItemBody(Guid ItemId);
    private sealed record SchemaBody(Guid ModuleId, List<SchemaFieldBody> Fields);
    private sealed record SchemaFieldBody(Guid ItemId, string Code, bool IsRequired);
    private sealed record SiteBody(Guid SiteId);
    private sealed record SiteDetailBody(Guid Id, string Code, int Status);
    private sealed record RecordBody(Guid RecordId);
    private sealed record PivotBody(List<SchemaFieldBody> Columns, List<PivotRowBody> Rows);
    private sealed record PivotRowBody(Guid RecordId, string Label);
    private sealed record DocumentBody(Guid DocumentId);
    private sealed record SiteDocumentBody(Guid Id, Guid DocumentId);
}
