using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Finance.Infrastructure.Persistence.Migrations.InitialFinance;

/// <inheritdoc />
public partial class InitialFinance : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.EnsureSchema(
            name: "finance");

        migrationBuilder.CreateTable(
            name: "boq_snapshots",
            schema: "finance",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                ProjectStageId = table.Column<Guid>(type: "uuid", nullable: true),
                Type = table.Column<int>(type: "integer", nullable: false),
                Version = table.Column<int>(type: "integer", nullable: false),
                GeneratedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                GeneratedById = table.Column<Guid>(type: "uuid", nullable: false),
                BudgetedTotal = table.Column<decimal>(type: "numeric(19,4)", nullable: false),
                PurchaseTotal = table.Column<decimal>(type: "numeric(19,4)", nullable: false),
                DifferenceTotal = table.Column<decimal>(type: "numeric(19,4)", nullable: false),
                ActualTotal = table.Column<decimal>(type: "numeric(19,4)", nullable: false),
                Notes = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                IsActive = table.Column<bool>(type: "boolean", nullable: false),
                CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
            },
            constraints: table => table.PrimaryKey("PK_boq_snapshots", x => x.Id));

        migrationBuilder.CreateTable(
            name: "cost_categories",
            schema: "finance",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                Code = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                Name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                Description = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                IsActive = table.Column<bool>(type: "boolean", nullable: false),
                CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
            },
            constraints: table => table.PrimaryKey("PK_cost_categories", x => x.Id));

        migrationBuilder.CreateTable(
            name: "invoices",
            schema: "finance",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                Number = table.Column<string>(type: "character varying(48)", maxLength: 48, nullable: false),
                ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                ProjectStageId = table.Column<Guid>(type: "uuid", nullable: true),
                Type = table.Column<int>(type: "integer", nullable: false),
                ClientId = table.Column<Guid>(type: "uuid", nullable: true),
                VendorName = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                IssuedOn = table.Column<DateOnly>(type: "date", nullable: false),
                DueOn = table.Column<DateOnly>(type: "date", nullable: true),
                TotalAmount = table.Column<decimal>(type: "numeric(19,4)", nullable: false, defaultValue: 0m),
                PaidAmount = table.Column<decimal>(type: "numeric(19,4)", nullable: false, defaultValue: 0m),
                Status = table.Column<int>(type: "integer", nullable: false),
                Note = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                IsActive = table.Column<bool>(type: "boolean", nullable: false),
                CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
            },
            constraints: table => table.PrimaryKey("PK_invoices", x => x.Id));

        migrationBuilder.CreateTable(
            name: "budget_lines",
            schema: "finance",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                Name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                Code = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                Description = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                AllocatedAmount = table.Column<decimal>(type: "numeric(19,4)", nullable: false, defaultValue: 0m),
                AdjustmentAllocation = table.Column<decimal>(type: "numeric(19,4)", nullable: false, defaultValue: 0m),
                CommittedAmount = table.Column<decimal>(type: "numeric(19,4)", nullable: false, defaultValue: 0m),
                ActualAmount = table.Column<decimal>(type: "numeric(19,4)", nullable: false, defaultValue: 0m),
                ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                CostCategoryId = table.Column<Guid>(type: "uuid", nullable: true),
                ProjectStageId = table.Column<Guid>(type: "uuid", nullable: true),
                ProjectTaskId = table.Column<Guid>(type: "uuid", nullable: true),
                IsActive = table.Column<bool>(type: "boolean", nullable: false),
                CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_budget_lines", x => x.Id);
                table.ForeignKey(
                    name: "FK_budget_lines_cost_categories_CostCategoryId",
                    column: x => x.CostCategoryId,
                    principalSchema: "finance",
                    principalTable: "cost_categories",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.SetNull);
            });

        migrationBuilder.CreateTable(
            name: "invoice_lines",
            schema: "finance",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                InvoiceId = table.Column<Guid>(type: "uuid", nullable: false),
                ProjectTaskId = table.Column<Guid>(type: "uuid", nullable: true),
                BudgetLineId = table.Column<Guid>(type: "uuid", nullable: true),
                Description = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                Quantity = table.Column<decimal>(type: "numeric(19,4)", nullable: false, defaultValue: 1m),
                Unit = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                UnitPrice = table.Column<decimal>(type: "numeric(19,4)", nullable: false),
                LineTotal = table.Column<decimal>(type: "numeric(19,4)", nullable: false),
                IsActive = table.Column<bool>(type: "boolean", nullable: false),
                CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_invoice_lines", x => x.Id);
                table.ForeignKey(
                    name: "FK_invoice_lines_budget_lines_BudgetLineId",
                    column: x => x.BudgetLineId,
                    principalSchema: "finance",
                    principalTable: "budget_lines",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_invoice_lines_invoices_InvoiceId",
                    column: x => x.InvoiceId,
                    principalSchema: "finance",
                    principalTable: "invoices",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "variations",
            schema: "finance",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                Kind = table.Column<int>(type: "integer", nullable: false),
                ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                BudgetLineId = table.Column<Guid>(type: "uuid", nullable: true),
                ProjectTaskId = table.Column<Guid>(type: "uuid", nullable: true),
                ProjectStageId = table.Column<Guid>(type: "uuid", nullable: true),
                Reason = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                Description = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                Amount = table.Column<decimal>(type: "numeric(19,4)", nullable: false),
                BudgetedAmount = table.Column<decimal>(type: "numeric(19,4)", nullable: true),
                ChosenAmount = table.Column<decimal>(type: "numeric(19,4)", nullable: true),
                Status = table.Column<int>(type: "integer", nullable: false),
                RequestedById = table.Column<Guid>(type: "uuid", nullable: false),
                ApprovedById = table.Column<Guid>(type: "uuid", nullable: true),
                ApprovedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                SentToClientAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                SignedByClientId = table.Column<Guid>(type: "uuid", nullable: true),
                SignedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                SignedDocumentPath = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                ResolutionNote = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                IsActive = table.Column<bool>(type: "boolean", nullable: false),
                CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_variations", x => x.Id);
                table.CheckConstraint("ck_variations_client_diff", "\"Kind\" <> 1 OR (\"ProjectTaskId\" IS NOT NULL AND \"BudgetedAmount\" IS NOT NULL AND \"ChosenAmount\" IS NOT NULL)");
                table.CheckConstraint("ck_variations_internal", "\"Kind\" <> 2 OR (\"BudgetLineId\" IS NOT NULL AND \"SignedByClientId\" IS NULL AND \"SignedDocumentPath\" IS NULL)");
                table.ForeignKey(
                    name: "FK_variations_budget_lines_BudgetLineId",
                    column: x => x.BudgetLineId,
                    principalSchema: "finance",
                    principalTable: "budget_lines",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "boq_snapshot_lines",
            schema: "finance",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                BoqSnapshotId = table.Column<Guid>(type: "uuid", nullable: false),
                Description = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                Quantity = table.Column<decimal>(type: "numeric(19,4)", nullable: true),
                Unit = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                BudgetedAmount = table.Column<decimal>(type: "numeric(19,4)", nullable: false),
                ActualAmount = table.Column<decimal>(type: "numeric(19,4)", nullable: false),
                DifferenceAmount = table.Column<decimal>(type: "numeric(19,4)", nullable: false),
                ProjectTaskId = table.Column<Guid>(type: "uuid", nullable: true),
                InvoiceLineId = table.Column<Guid>(type: "uuid", nullable: true),
                VariationId = table.Column<Guid>(type: "uuid", nullable: true),
                IsActive = table.Column<bool>(type: "boolean", nullable: false),
                CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_boq_snapshot_lines", x => x.Id);
                table.ForeignKey(
                    name: "FK_boq_snapshot_lines_boq_snapshots_BoqSnapshotId",
                    column: x => x.BoqSnapshotId,
                    principalSchema: "finance",
                    principalTable: "boq_snapshots",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "FK_boq_snapshot_lines_invoice_lines_InvoiceLineId",
                    column: x => x.InvoiceLineId,
                    principalSchema: "finance",
                    principalTable: "invoice_lines",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_boq_snapshot_lines_variations_VariationId",
                    column: x => x.VariationId,
                    principalSchema: "finance",
                    principalTable: "variations",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "ledger_entries",
            schema: "finance",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                ProjectStageId = table.Column<Guid>(type: "uuid", nullable: true),
                ProjectTaskId = table.Column<Guid>(type: "uuid", nullable: true),
                BudgetLineId = table.Column<Guid>(type: "uuid", nullable: true),
                Direction = table.Column<int>(type: "integer", nullable: false),
                Type = table.Column<int>(type: "integer", nullable: false),
                Amount = table.Column<decimal>(type: "numeric(19,4)", nullable: false),
                Method = table.Column<int>(type: "integer", nullable: true),
                ClientId = table.Column<Guid>(type: "uuid", nullable: true),
                VendorName = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                VariationId = table.Column<Guid>(type: "uuid", nullable: true),
                ReversalOfId = table.Column<Guid>(type: "uuid", nullable: true),
                OccurredOn = table.Column<DateOnly>(type: "date", nullable: false),
                Reference = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                Note = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                RecordedById = table.Column<Guid>(type: "uuid", nullable: false),
                IsActive = table.Column<bool>(type: "boolean", nullable: false),
                CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_ledger_entries", x => x.Id);
                table.ForeignKey(
                    name: "FK_ledger_entries_budget_lines_BudgetLineId",
                    column: x => x.BudgetLineId,
                    principalSchema: "finance",
                    principalTable: "budget_lines",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_ledger_entries_ledger_entries_ReversalOfId",
                    column: x => x.ReversalOfId,
                    principalSchema: "finance",
                    principalTable: "ledger_entries",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_ledger_entries_variations_VariationId",
                    column: x => x.VariationId,
                    principalSchema: "finance",
                    principalTable: "variations",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "ledger_allocations",
            schema: "finance",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                LedgerEntryId = table.Column<Guid>(type: "uuid", nullable: false),
                InvoiceId = table.Column<Guid>(type: "uuid", nullable: false),
                Amount = table.Column<decimal>(type: "numeric(19,4)", nullable: false),
                IsActive = table.Column<bool>(type: "boolean", nullable: false),
                CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_ledger_allocations", x => x.Id);
                table.ForeignKey(
                    name: "FK_ledger_allocations_invoices_InvoiceId",
                    column: x => x.InvoiceId,
                    principalSchema: "finance",
                    principalTable: "invoices",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_ledger_allocations_ledger_entries_LedgerEntryId",
                    column: x => x.LedgerEntryId,
                    principalSchema: "finance",
                    principalTable: "ledger_entries",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "ix_boq_snapshot_lines_boq_snapshot_id",
            schema: "finance",
            table: "boq_snapshot_lines",
            column: "BoqSnapshotId");

        migrationBuilder.CreateIndex(
            name: "ix_boq_snapshot_lines_invoice_line_id",
            schema: "finance",
            table: "boq_snapshot_lines",
            column: "InvoiceLineId");

        migrationBuilder.CreateIndex(
            name: "ix_boq_snapshot_lines_project_task_id",
            schema: "finance",
            table: "boq_snapshot_lines",
            column: "ProjectTaskId");

        migrationBuilder.CreateIndex(
            name: "ix_boq_snapshot_lines_variation_id",
            schema: "finance",
            table: "boq_snapshot_lines",
            column: "VariationId");

        migrationBuilder.CreateIndex(
            name: "ix_boq_snapshots_project_id_version",
            schema: "finance",
            table: "boq_snapshots",
            columns: new[] { "ProjectId", "Version" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "ix_boq_snapshots_project_stage_id",
            schema: "finance",
            table: "boq_snapshots",
            column: "ProjectStageId");

        migrationBuilder.CreateIndex(
            name: "ix_budget_lines_cost_category_id",
            schema: "finance",
            table: "budget_lines",
            column: "CostCategoryId");

        migrationBuilder.CreateIndex(
            name: "ix_budget_lines_project_id",
            schema: "finance",
            table: "budget_lines",
            column: "ProjectId");

        migrationBuilder.CreateIndex(
            name: "ix_budget_lines_project_stage_id",
            schema: "finance",
            table: "budget_lines",
            column: "ProjectStageId");

        migrationBuilder.CreateIndex(
            name: "ix_budget_lines_project_task_id",
            schema: "finance",
            table: "budget_lines",
            column: "ProjectTaskId");

        migrationBuilder.CreateIndex(
            name: "ix_cost_categories_code",
            schema: "finance",
            table: "cost_categories",
            column: "Code",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "ix_invoice_lines_budget_line_id",
            schema: "finance",
            table: "invoice_lines",
            column: "BudgetLineId");

        migrationBuilder.CreateIndex(
            name: "ix_invoice_lines_invoice_id",
            schema: "finance",
            table: "invoice_lines",
            column: "InvoiceId");

        migrationBuilder.CreateIndex(
            name: "ix_invoice_lines_project_task_id",
            schema: "finance",
            table: "invoice_lines",
            column: "ProjectTaskId");

        migrationBuilder.CreateIndex(
            name: "ix_invoices_client_id",
            schema: "finance",
            table: "invoices",
            column: "ClientId");

        migrationBuilder.CreateIndex(
            name: "ix_invoices_due_on",
            schema: "finance",
            table: "invoices",
            column: "DueOn");

        migrationBuilder.CreateIndex(
            name: "ix_invoices_number",
            schema: "finance",
            table: "invoices",
            column: "Number",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "ix_invoices_project_id_status",
            schema: "finance",
            table: "invoices",
            columns: new[] { "ProjectId", "Status" });

        migrationBuilder.CreateIndex(
            name: "ix_invoices_project_stage_id",
            schema: "finance",
            table: "invoices",
            column: "ProjectStageId");

        migrationBuilder.CreateIndex(
            name: "ix_ledger_allocations_invoice_id",
            schema: "finance",
            table: "ledger_allocations",
            column: "InvoiceId");

        migrationBuilder.CreateIndex(
            name: "ix_ledger_allocations_ledger_entry_id",
            schema: "finance",
            table: "ledger_allocations",
            column: "LedgerEntryId");

        migrationBuilder.CreateIndex(
            name: "ix_ledger_entries_budget_line_id",
            schema: "finance",
            table: "ledger_entries",
            column: "BudgetLineId");

        migrationBuilder.CreateIndex(
            name: "ix_ledger_entries_client_id",
            schema: "finance",
            table: "ledger_entries",
            column: "ClientId");

        migrationBuilder.CreateIndex(
            name: "ix_ledger_entries_project_id_occurred_on",
            schema: "finance",
            table: "ledger_entries",
            columns: new[] { "ProjectId", "OccurredOn" });

        migrationBuilder.CreateIndex(
            name: "ix_ledger_entries_project_stage_id",
            schema: "finance",
            table: "ledger_entries",
            column: "ProjectStageId");

        migrationBuilder.CreateIndex(
            name: "ix_ledger_entries_project_task_id",
            schema: "finance",
            table: "ledger_entries",
            column: "ProjectTaskId");

        migrationBuilder.CreateIndex(
            name: "ix_ledger_entries_reversal_of_id",
            schema: "finance",
            table: "ledger_entries",
            column: "ReversalOfId");

        migrationBuilder.CreateIndex(
            name: "ix_ledger_entries_type",
            schema: "finance",
            table: "ledger_entries",
            column: "Type");

        migrationBuilder.CreateIndex(
            name: "ix_ledger_entries_variation_id",
            schema: "finance",
            table: "ledger_entries",
            column: "VariationId");

        migrationBuilder.CreateIndex(
            name: "ix_variations_budget_line_id",
            schema: "finance",
            table: "variations",
            column: "BudgetLineId");

        migrationBuilder.CreateIndex(
            name: "ix_variations_kind",
            schema: "finance",
            table: "variations",
            column: "Kind");

        migrationBuilder.CreateIndex(
            name: "ix_variations_project_id_status",
            schema: "finance",
            table: "variations",
            columns: new[] { "ProjectId", "Status" });

        migrationBuilder.CreateIndex(
            name: "ix_variations_project_stage_id",
            schema: "finance",
            table: "variations",
            column: "ProjectStageId");

        migrationBuilder.CreateIndex(
            name: "ix_variations_project_task_id",
            schema: "finance",
            table: "variations",
            column: "ProjectTaskId");

        migrationBuilder.CreateIndex(
            name: "ix_variations_signed_by_client_id",
            schema: "finance",
            table: "variations",
            column: "SignedByClientId");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "boq_snapshot_lines",
            schema: "finance");

        migrationBuilder.DropTable(
            name: "ledger_allocations",
            schema: "finance");

        migrationBuilder.DropTable(
            name: "boq_snapshots",
            schema: "finance");

        migrationBuilder.DropTable(
            name: "invoice_lines",
            schema: "finance");

        migrationBuilder.DropTable(
            name: "ledger_entries",
            schema: "finance");

        migrationBuilder.DropTable(
            name: "invoices",
            schema: "finance");

        migrationBuilder.DropTable(
            name: "variations",
            schema: "finance");

        migrationBuilder.DropTable(
            name: "budget_lines",
            schema: "finance");

        migrationBuilder.DropTable(
            name: "cost_categories",
            schema: "finance");
    }
}
