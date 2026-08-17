using System.Reflection;
using BuildingBlocks.Outbox;
using BuildingBlocks.Web;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;
using Xunit;

namespace Architecture.Tests;

/// <summary>
/// Rule 9: every cross-schema foreign key points in an allowed module dependency direction. Needs a
/// real, migrated Postgres to query <c>pg_constraint</c> against — implemented here, per PLAN.md's own
/// text, rather than at G0-07 where no fixture existed yet.
///
/// Non-vacuous since G2-03: <c>hr.employees."AppUserId" → identity.users("Id")</c> is the first real
/// cross-schema FK this checks. Migrates every module in <see cref="ModuleDependencyOrder.SchemaOrder"/>
/// via each module's own (public) <see cref="IModule.Register"/> — the same throwaway-provider
/// technique <c>Integration.Tests.ApiFixture</c> uses (G1-07), so this test doesn't need to name any
/// module's internal DbContext type directly.
///
/// The allow-list is the full ten-module graph from docs/cross-schema-foreign-keys.md, not trimmed to
/// this solution's Identity+HR scope — harmless, since pairs for modules that don't exist yet simply
/// never appear in the query results until those modules are built.
/// </summary>
public sealed class Rule9CrossSchemaForeignKeyTests : IAsyncLifetime
{
    private static readonly (string From, string To)[] AllowedPairs =
    [
        ("hr", "identity"),
        ("approvals", "identity"),
        ("crm", "identity"), ("crm", "hr"),
        // S1-05: sites -> documents and sites -> hr are new. Both are legal because sites is the
        // LAST schema in SchemaOrder, so every schema it references already exists when it
        // migrates -- contrast crm.crm_artifact_documents.DocumentId, which can never carry an FK
        // at all because crm migrates before documents. Same link shape, opposite outcome, and the
        // reason is entirely migration order. See docs/cross-schema-foreign-keys.md.
        ("sites", "projects"), ("sites", "crm"), ("sites", "documents"), ("sites", "hr"),
        ("projects", "identity"), ("projects", "hr"), ("projects", "crm"), ("projects", "approvals"),
        ("finance", "projects"), ("finance", "crm"),
        ("documents", "projects"), ("documents", "hr"), ("documents", "approvals"),
        ("notify", "identity"),
    ];

    private static readonly IReadOnlyDictionary<string, (string AssemblyName, string ContextTypeName)> Modules =
        new Dictionary<string, (string, string)>
        {
            ["identity"] = ("Identity.Infrastructure", "Identity.Infrastructure.Persistence.IdentityModuleDbContext, Identity.Infrastructure"),
            ["hr"] = ("Hr.Infrastructure", "Hr.Infrastructure.Persistence.HrDbContext, Hr.Infrastructure"),
            ["approvals"] = ("Approvals.Infrastructure", "Approvals.Infrastructure.Persistence.ApprovalsDbContext, Approvals.Infrastructure"),
            ["crm"] = ("Crm.Infrastructure", "Crm.Infrastructure.Persistence.CrmDbContext, Crm.Infrastructure"),
            ["projects"] = ("Projects.Infrastructure", "Projects.Infrastructure.Persistence.ProjectsDbContext, Projects.Infrastructure"),
            ["documents"] = ("Documents.Infrastructure", "Documents.Infrastructure.Persistence.DocumentsDbContext, Documents.Infrastructure"),
            ["sites"] = ("Sites.Infrastructure", "Sites.Infrastructure.Persistence.SitesDbContext, Sites.Infrastructure"),
            ["notify"] = ("Notifications.Infrastructure", "Notifications.Infrastructure.Persistence.NotificationsDbContext, Notifications.Infrastructure"),
        };

    private PostgreSqlContainer _container = null!;

    public async Task InitializeAsync()
    {
        _container = new PostgreSqlBuilder("postgres:17").Build();
        await _container.StartAsync();

        await using var shared = new SharedDbContext(
            new DbContextOptionsBuilder<SharedDbContext>().UseNpgsql(_container.GetConnectionString()).Options);
        await shared.Database.MigrateAsync();

        foreach (var schema in ModuleDependencyOrder.SchemaOrder)
        {
            if (!Modules.TryGetValue(schema, out var module))
                continue;

            await MigrateModuleSchemaAsync(module.AssemblyName, module.ContextTypeName);
        }
    }

    public async Task DisposeAsync() => await _container.DisposeAsync();

    [Fact]
    public async Task EveryCrossSchemaForeignKey_PointsInAnAllowedDirection()
    {
        await using var shared = new SharedDbContext(
            new DbContextOptionsBuilder<SharedDbContext>().UseNpgsql(_container.GetConnectionString()).Options);

        const string sql = """
            SELECT src_ns.nspname AS from_schema, tgt_ns.nspname AS to_schema, con.conname AS constraint_name
            FROM pg_constraint con
            JOIN pg_class src ON src.oid = con.conrelid
            JOIN pg_namespace src_ns ON src_ns.oid = src.relnamespace
            JOIN pg_class tgt ON tgt.oid = con.confrelid
            JOIN pg_namespace tgt_ns ON tgt_ns.oid = tgt.relnamespace
            WHERE con.contype = 'f' AND src_ns.nspname <> tgt_ns.nspname;
            """;

        await using var connection = shared.Database.GetDbConnection();
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = sql;

        await using var reader = await command.ExecuteReaderAsync();

        var found = new List<(string From, string To, string Name)>();
        var violations = new List<string>();
        while (await reader.ReadAsync())
        {
            var from = reader.GetString(0);
            var to = reader.GetString(1);
            var name = reader.GetString(2);
            found.Add((from, to, name));

            if (!AllowedPairs.Contains((from, to)))
                violations.Add($"{name}: {from} -> {to}");
        }

        Assert.True(violations.Count == 0,
            $"Cross-schema foreign keys pointing the wrong way: {string.Join(", ", violations)}");

        // Non-vacuity check (T4-style): confirm the FKs this solution actually has today were found, not
        // just that none were found wrong-way-round - an empty result set trivially satisfies the
        // assertion above without proving anything.
        Assert.Contains(found, fk => fk is ("hr", "identity", "fk_employees_app_user"));
        Assert.Contains(found, fk => fk is ("approvals", "identity", "fk_approvals_requested_by_user"));
        Assert.Contains(found, fk => fk is ("projects", "hr", "fk_projects_owner"));
        Assert.Contains(found, fk => fk is ("projects", "approvals", "fk_gate_conditions_approval"));

        // G7-04: all seven documents -> projects/hr FKs, not just a representative sample - the plan's
        // own verify text asks for every one to be present.
        Assert.Contains(found, fk => fk is ("documents", "projects", "fk_project_documents_project"));
        Assert.Contains(found, fk => fk is ("documents", "projects", "fk_project_documents_project_stage"));
        Assert.Contains(found, fk => fk is ("documents", "projects", "fk_project_documents_project_task"));
        Assert.Contains(found, fk => fk is ("documents", "hr", "fk_project_documents_uploaded_by"));
        Assert.Contains(found, fk => fk is ("documents", "projects", "fk_document_folders_project"));
        Assert.Contains(found, fk => fk is ("documents", "projects", "fk_document_folders_project_stage"));
        Assert.Contains(found, fk => fk is ("documents", "projects", "fk_document_folders_project_task"));

        // G9-08: all nine crm -> identity/hr FKs, not just a representative sample - same discipline
        // as G7-04's Documents assertions above.
        Assert.Contains(found, fk => fk is ("crm", "identity", "fk_clients_app_user"));
        Assert.Contains(found, fk => fk is ("crm", "hr", "fk_crm_leads_owner"));
        Assert.Contains(found, fk => fk is ("crm", "hr", "fk_crm_meetings_requested_by_employee"));
        Assert.Contains(found, fk => fk is ("crm", "hr", "fk_crm_meeting_slots_proposed_by_employee"));
        Assert.Contains(found, fk => fk is ("crm", "hr", "fk_crm_design_briefs_created_by"));
        Assert.Contains(found, fk => fk is ("crm", "hr", "fk_crm_brief_spaces_created_by"));
        Assert.Contains(found, fk => fk is ("crm", "hr", "fk_crm_requirement_periods_opened_by"));
        Assert.Contains(found, fk => fk is ("crm", "hr", "fk_crm_requirement_items_created_by_employee"));
        Assert.Contains(found, fk => fk is ("crm", "hr", "fk_crm_client_notes_written_by"));
        Assert.Contains(found, fk => fk is ("crm", "hr", "fk_crm_artifact_documents_added_by_employee"));

        // Confirms crm -> projects and crm -> documents are genuinely ABSENT, not merely un-asserted -
        // structural item 1's migration-order cycle (crm precedes projects in SchemaOrder) means no
        // such FK can exist; this fails loudly if a future change ever adds one, since crm -> projects
        // is not in AllowedPairs above.
        Assert.DoesNotContain(found, fk => fk.From == "crm" && fk.To == "projects");
        Assert.DoesNotContain(found, fk => fk.From == "crm" && fk.To == "documents");

        // N1-03: both notify -> identity FKs, raw SQL in NotificationsCrossSchemaForeignKeys. The
        // ("notify", "identity") pair has been in AllowedPairs since before the module existed, so
        // without these two lines it would stay permissive and unexercised - allow-listed but never
        // proven to allow anything.
        Assert.Contains(found, fk => fk is ("notify", "identity", "fk_notifications_recipient_user"));
        Assert.Contains(found, fk => fk is ("notify", "identity", "fk_activities_actor_user"));

        // N1-02: shared.audit_logs.user_id carries NO foreign key, and the absence is asserted rather
        // than intended. Two independent reasons it cannot and must not: shared migrates FIRST, so a
        // constraint pointing at identity is structurally impossible; and an audit row must outlive its
        // subject, whereas RESTRICT would make deleting a user fail BECAUSE that user did something
        // auditable. "shared" is not in AllowedPairs, so any future FK out of it fails the main
        // assertion too - this makes the intent explicit at the point someone would be tempted.
        Assert.DoesNotContain(found, fk => fk.From == "shared");
    }

    private async Task MigrateModuleSchemaAsync(string moduleAssemblyName, string assemblyQualifiedContextTypeName)
    {
        var module = ModuleDiscovery.DiscoverModules(Assembly.Load(moduleAssemblyName)).Single();

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = _container.GetConnectionString(),
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        module.Register(services, configuration);

        await using var provider = services.BuildServiceProvider();

        var contextType = Type.GetType(assemblyQualifiedContextTypeName)
            ?? throw new InvalidOperationException($"Could not load type '{assemblyQualifiedContextTypeName}'.");

        using var scope = provider.CreateScope();
        var context = (DbContext)scope.ServiceProvider.GetRequiredService(contextType);
        await context.Database.MigrateAsync();
    }
}
