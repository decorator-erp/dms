using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Finance.Infrastructure.Persistence.Migrations.InitialFinance
{
    /// <summary>
    /// Finance's 22 cross-schema foreign keys — the most of any module so far, and all of them
    /// outbound. EF cannot emit any of them (the two entities are in different models), so each is raw
    /// SQL with quoted PascalCase columns and <c>ON DELETE RESTRICT</c>.
    ///
    /// <para><b>All 22 are legal because finance migrates ninth</b>, after projects, crm and hr. That is
    /// the whole test, applied as <c>docs/cross-schema-foreign-keys.md</c> states it: every schema
    /// referenced here already exists when this runs. Nothing points back into finance, so the position
    /// costs nothing to anyone else — see <c>ModuleDependencyOrder</c>.</para>
    ///
    /// <para><b>The one link that deliberately gets NO foreign key is not in this file at all</b>, which
    /// is why it is worth naming here: <c>projects.gate_conditions.VariationId</c> (F1-00) points the
    /// other way, from projects into finance, and a constraint for it would require
    /// <c>projects &lt; finance</c> while these 22 require <c>finance &lt; projects</c>. No ordering
    /// satisfies both. It is the fourth instance of the deliberately-absent-FK pattern and the first
    /// where the absence is forced by a cycle this module itself creates.</para>
    ///
    /// <para><b>RESTRICT throughout, and it changes behaviour.</b> Rule 9 permits nothing else across a
    /// schema boundary. The practical effect is that a project, client or employee carrying finance
    /// history cannot be deleted until that history is dealt with — which for a ledger is the correct
    /// answer rather than a limitation: money records are append-only, corrections are reversal rows,
    /// and a cascade would delete the audit trail along with the row that explains it.</para>
    ///
    /// <para><b>Every constrained column is indexed.</b> Eighteen were indexed in the initial migration
    /// (or lead a composite index there); the four <c>hr.employees</c> columns are indexed by this
    /// migration, immediately above the constraints that need them.</para>
    /// </summary>
    public partial class FinanceCrossSchemaForeignKeys : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ── Indexes for the four hr.employees columns, ahead of their constraints ──────────────
            migrationBuilder.CreateIndex(
                name: "ix_variations_approved_by_id",
                schema: "finance",
                table: "variations",
                column: "ApprovedById");

            migrationBuilder.CreateIndex(
                name: "ix_variations_requested_by_id",
                schema: "finance",
                table: "variations",
                column: "RequestedById");

            migrationBuilder.CreateIndex(
                name: "ix_ledger_entries_recorded_by_id",
                schema: "finance",
                table: "ledger_entries",
                column: "RecordedById");

            migrationBuilder.CreateIndex(
                name: "ix_boq_snapshots_generated_by_id",
                schema: "finance",
                table: "boq_snapshots",
                column: "GeneratedById");

            // ── finance -> projects (allow-listed since the plan was written) ──────────────────────
            // Five tables carry ProjectId, all NOT NULL: everything financial belongs to a project.
            AddForeignKey(migrationBuilder, "budget_lines", "fk_budget_lines_project", "ProjectId", "projects.projects");
            AddForeignKey(migrationBuilder, "ledger_entries", "fk_ledger_entries_project", "ProjectId", "projects.projects");
            AddForeignKey(migrationBuilder, "invoices", "fk_invoices_project", "ProjectId", "projects.projects");
            AddForeignKey(migrationBuilder, "variations", "fk_variations_project", "ProjectId", "projects.projects");
            AddForeignKey(migrationBuilder, "boq_snapshots", "fk_boq_snapshots_project", "ProjectId", "projects.projects");

            AddForeignKey(migrationBuilder, "budget_lines", "fk_budget_lines_project_stage", "ProjectStageId", "projects.project_stages");
            AddForeignKey(migrationBuilder, "ledger_entries", "fk_ledger_entries_project_stage", "ProjectStageId", "projects.project_stages");
            AddForeignKey(migrationBuilder, "invoices", "fk_invoices_project_stage", "ProjectStageId", "projects.project_stages");
            AddForeignKey(migrationBuilder, "variations", "fk_variations_project_stage", "ProjectStageId", "projects.project_stages");
            AddForeignKey(migrationBuilder, "boq_snapshots", "fk_boq_snapshots_project_stage", "ProjectStageId", "projects.project_stages");

            AddForeignKey(migrationBuilder, "budget_lines", "fk_budget_lines_project_task", "ProjectTaskId", "projects.project_tasks");
            AddForeignKey(migrationBuilder, "ledger_entries", "fk_ledger_entries_project_task", "ProjectTaskId", "projects.project_tasks");
            AddForeignKey(migrationBuilder, "invoice_lines", "fk_invoice_lines_project_task", "ProjectTaskId", "projects.project_tasks");
            AddForeignKey(migrationBuilder, "variations", "fk_variations_project_task", "ProjectTaskId", "projects.project_tasks");
            AddForeignKey(migrationBuilder, "boq_snapshot_lines", "fk_boq_snapshot_lines_project_task", "ProjectTaskId", "projects.project_tasks");

            // ── finance -> crm (allow-listed since the plan was written) ───────────────────────────
            // Who paid, who is billed, and who signed a client difference.
            AddForeignKey(migrationBuilder, "ledger_entries", "fk_ledger_entries_client", "ClientId", "crm.clients");
            AddForeignKey(migrationBuilder, "invoices", "fk_invoices_client", "ClientId", "crm.clients");
            AddForeignKey(migrationBuilder, "variations", "fk_variations_signed_by_client", "SignedByClientId", "crm.clients");

            // ── finance -> hr (NEW pair) ──────────────────────────────────────────────────────────
            // Four "who did this" stamps. RequestedById and ApprovedById are the same target from the
            // same table: who raised the variation and who decided it -- and ApprovedById is stamped on
            // rejection too, so it is the decider either way, not only the approver.
            AddForeignKey(migrationBuilder, "ledger_entries", "fk_ledger_entries_recorded_by", "RecordedById", "hr.employees");
            AddForeignKey(migrationBuilder, "variations", "fk_variations_requested_by", "RequestedById", "hr.employees");
            AddForeignKey(migrationBuilder, "variations", "fk_variations_approved_by", "ApprovedById", "hr.employees");
            AddForeignKey(migrationBuilder, "boq_snapshots", "fk_boq_snapshots_generated_by", "GeneratedById", "hr.employees");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            foreach (var (table, constraint) in new[]
            {
                ("budget_lines", "fk_budget_lines_project"),
                ("ledger_entries", "fk_ledger_entries_project"),
                ("invoices", "fk_invoices_project"),
                ("variations", "fk_variations_project"),
                ("boq_snapshots", "fk_boq_snapshots_project"),
                ("budget_lines", "fk_budget_lines_project_stage"),
                ("ledger_entries", "fk_ledger_entries_project_stage"),
                ("invoices", "fk_invoices_project_stage"),
                ("variations", "fk_variations_project_stage"),
                ("boq_snapshots", "fk_boq_snapshots_project_stage"),
                ("budget_lines", "fk_budget_lines_project_task"),
                ("ledger_entries", "fk_ledger_entries_project_task"),
                ("invoice_lines", "fk_invoice_lines_project_task"),
                ("variations", "fk_variations_project_task"),
                ("boq_snapshot_lines", "fk_boq_snapshot_lines_project_task"),
                ("ledger_entries", "fk_ledger_entries_client"),
                ("invoices", "fk_invoices_client"),
                ("variations", "fk_variations_signed_by_client"),
                ("ledger_entries", "fk_ledger_entries_recorded_by"),
                ("variations", "fk_variations_requested_by"),
                ("variations", "fk_variations_approved_by"),
                ("boq_snapshots", "fk_boq_snapshots_generated_by"),
            })
            {
                migrationBuilder.Sql($"ALTER TABLE finance.{table} DROP CONSTRAINT {constraint};");
            }

            migrationBuilder.DropIndex(
                name: "ix_variations_approved_by_id",
                schema: "finance",
                table: "variations");

            migrationBuilder.DropIndex(
                name: "ix_variations_requested_by_id",
                schema: "finance",
                table: "variations");

            migrationBuilder.DropIndex(
                name: "ix_ledger_entries_recorded_by_id",
                schema: "finance",
                table: "ledger_entries");

            migrationBuilder.DropIndex(
                name: "ix_boq_snapshots_generated_by_id",
                schema: "finance",
                table: "boq_snapshots");
        }

        /// <summary>
        /// One shape for all 22, because they genuinely are one shape and writing them out longhand
        /// would bury the four that differ (target table) in 130 lines of identical text.
        ///
        /// <para><b>Column names are quoted; table names are not.</b> That asymmetry is deliberate and
        /// is the failure this repo has hit before: this solution configures no snake_case naming
        /// convention, so columns are PascalCase and an unquoted <c>ProjectId</c> would be folded to
        /// <c>projectid</c> and reference a column that does not exist. Table names really are
        /// lowercase, so they need no quoting.</para>
        /// </summary>
        private static void AddForeignKey(
            MigrationBuilder migrationBuilder, string table, string constraint, string column, string target) =>
            migrationBuilder.Sql($"""
                ALTER TABLE finance.{table}
                    ADD CONSTRAINT {constraint}
                    FOREIGN KEY ("{column}") REFERENCES {target} ("Id")
                    ON DELETE RESTRICT;
                """);
    }
}
