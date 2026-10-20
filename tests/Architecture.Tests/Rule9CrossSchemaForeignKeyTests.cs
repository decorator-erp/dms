using System.Data.Common;
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
        // F1-05: ("finance","hr") is NEW; the other two were allow-listed phases before the module
        // existed. Finance stamps four "who did this" columns against hr.employees -- RecordedById on
        // a ledger entry, RequestedById and ApprovedById on a variation, GeneratedById on a BOQ
        // snapshot -- and all four now carry real constraints, because finance migrates ninth and hr
        // is third. 22 outbound foreign keys in total, the most of any module; nothing points back in.
        ("finance", "projects"), ("finance", "crm"), ("finance", "hr"),
        ("documents", "projects"), ("documents", "hr"), ("documents", "approvals"),
        ("notify", "identity"),
    ];

    // Internal so Rule 12 (which needs a fully migrated database to boot the real host) migrates from the
    // same map: a module added here is migrated there too.
    internal static readonly IReadOnlyDictionary<string, (string AssemblyName, string ContextTypeName)> Modules =
        new Dictionary<string, (string, string)>
        {
            ["identity"] = ("Identity.Infrastructure", "Identity.Infrastructure.Persistence.IdentityModuleDbContext, Identity.Infrastructure"),
            ["hr"] = ("Hr.Infrastructure", "Hr.Infrastructure.Persistence.HrDbContext, Hr.Infrastructure"),
            ["approvals"] = ("Approvals.Infrastructure", "Approvals.Infrastructure.Persistence.ApprovalsDbContext, Approvals.Infrastructure"),
            ["crm"] = ("Crm.Infrastructure", "Crm.Infrastructure.Persistence.CrmDbContext, Crm.Infrastructure"),
            ["projects"] = ("Projects.Infrastructure", "Projects.Infrastructure.Persistence.ProjectsDbContext, Projects.Infrastructure"),
            ["documents"] = ("Documents.Infrastructure", "Documents.Infrastructure.Persistence.DocumentsDbContext, Documents.Infrastructure"),
            ["sites"] = ("Sites.Infrastructure", "Sites.Infrastructure.Persistence.SitesDbContext, Sites.Infrastructure"),
            // F1-05. Without this entry the loop in InitializeAsync hits `continue` for "finance", the
            // schema is never migrated, and every finance constraint is simply absent from the query
            // results -- so the rule reports success over 22 foreign keys it never saw. The allow-list
            // entry above is inert on its own; this is the line that makes it mean anything.
            ["finance"] = ("Finance.Infrastructure", "Finance.Infrastructure.Persistence.FinanceDbContext, Finance.Infrastructure"),
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

        foreach (string schema in ModuleDependencyOrder.SchemaOrder)
        {
            if (!Modules.TryGetValue(schema, out (string AssemblyName, string ContextTypeName) module))
            {
                continue;
            }

            await MigrateModuleSchemaAsync(_container.GetConnectionString(), module.AssemblyName, module.ContextTypeName);
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

        await using DbConnection connection = shared.Database.GetDbConnection();
        await connection.OpenAsync();

        await using DbCommand command = connection.CreateCommand();
        command.CommandText = sql;

        await using DbDataReader reader = await command.ExecuteReaderAsync();

        var found = new List<(string From, string To, string Name)>();
        var violations = new List<string>();
        while (await reader.ReadAsync())
        {
            string from = reader.GetString(0);
            string to = reader.GetString(1);
            string name = reader.GetString(2);
            found.Add((from, to, name));

            if (!AllowedPairs.Contains((from, to)))
            {
                violations.Add($"{name}: {from} -> {to}");
            }
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

        // F1-05: all 22 finance foreign keys, enumerated from the entities rather than sampled - the
        // G7-04/G9-08 discipline, and the module with the most to enumerate.
        //
        // finance -> projects, fifteen of them across three target tables.
        Assert.Contains(found, fk => fk is ("finance", "projects", "fk_budget_lines_project"));
        Assert.Contains(found, fk => fk is ("finance", "projects", "fk_ledger_entries_project"));
        Assert.Contains(found, fk => fk is ("finance", "projects", "fk_invoices_project"));
        Assert.Contains(found, fk => fk is ("finance", "projects", "fk_variations_project"));
        Assert.Contains(found, fk => fk is ("finance", "projects", "fk_boq_snapshots_project"));
        Assert.Contains(found, fk => fk is ("finance", "projects", "fk_budget_lines_project_stage"));
        Assert.Contains(found, fk => fk is ("finance", "projects", "fk_ledger_entries_project_stage"));
        Assert.Contains(found, fk => fk is ("finance", "projects", "fk_invoices_project_stage"));
        Assert.Contains(found, fk => fk is ("finance", "projects", "fk_variations_project_stage"));
        Assert.Contains(found, fk => fk is ("finance", "projects", "fk_boq_snapshots_project_stage"));
        Assert.Contains(found, fk => fk is ("finance", "projects", "fk_budget_lines_project_task"));
        Assert.Contains(found, fk => fk is ("finance", "projects", "fk_ledger_entries_project_task"));
        Assert.Contains(found, fk => fk is ("finance", "projects", "fk_invoice_lines_project_task"));
        Assert.Contains(found, fk => fk is ("finance", "projects", "fk_variations_project_task"));
        Assert.Contains(found, fk => fk is ("finance", "projects", "fk_boq_snapshot_lines_project_task"));

        // finance -> crm: who paid, who is billed, who signed.
        Assert.Contains(found, fk => fk is ("finance", "crm", "fk_ledger_entries_client"));
        Assert.Contains(found, fk => fk is ("finance", "crm", "fk_invoices_client"));
        Assert.Contains(found, fk => fk is ("finance", "crm", "fk_variations_signed_by_client"));

        // finance -> hr, the NEW pair: four "who did this" stamps.
        Assert.Contains(found, fk => fk is ("finance", "hr", "fk_ledger_entries_recorded_by"));
        Assert.Contains(found, fk => fk is ("finance", "hr", "fk_variations_requested_by"));
        Assert.Contains(found, fk => fk is ("finance", "hr", "fk_variations_approved_by"));
        Assert.Contains(found, fk => fk is ("finance", "hr", "fk_boq_snapshots_generated_by"));

        // F1-00/F1-05: projects.gate_conditions."VariationId" carries NO foreign key, and the absence is
        // asserted rather than merely intended -- the fourth instance of the pattern, and the first
        // where the module creating the cycle is the one being constrained. The 22 assertions above all
        // require finance AFTER projects; a constraint for VariationId would require projects AFTER
        // finance. No ordering satisfies both, so this direction can never be constrained while those
        // exist. ("projects","finance") is not in AllowedPairs either, so adding one would fail the main
        // assertion too; this line names the intent where someone would be tempted to "fix" it.
        Assert.DoesNotContain(found, fk => fk.From == "projects" && fk.To == "finance");

        // And nothing at all points into finance, from anywhere. Stronger than the line above and the
        // property the SchemaOrder insertion actually relies on: it is what makes finance's position
        // free rather than forced.
        Assert.DoesNotContain(found, fk => fk.To == "finance");
    }

    internal static async Task MigrateModuleSchemaAsync(
        string connectionString, string moduleAssemblyName, string assemblyQualifiedContextTypeName)
    {
        IModule module = ModuleDiscovery.DiscoverModules(Assembly.Load(moduleAssemblyName)).Single();

        IConfigurationRoot configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = connectionString,
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        module.Register(services, configuration);

        await using ServiceProvider provider = services.BuildServiceProvider();

        Type contextType = Type.GetType(assemblyQualifiedContextTypeName)
            ?? throw new InvalidOperationException($"Could not load type '{assemblyQualifiedContextTypeName}'.");

        using IServiceScope scope = provider.CreateScope();
        var context = (DbContext)scope.ServiceProvider.GetRequiredService(contextType);
        await context.Database.MigrateAsync();
    }
}
