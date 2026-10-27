using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Crm.Infrastructure.Persistence.Migrations.InitialCrm;

/// <summary>
/// PR A of the CRM backend completion (items, periods, spaces). One additive migration:
/// <list type="bullet">
/// <item><description>A-6: <c>SupersedesItemId</c> becomes unique where set — the item ledger cannot fork.</description></item>
/// <item><description>A-9: <c>xmin</c> becomes the concurrency token of items and periods. No-op in SQL: <c>xmin</c>
/// is Postgres' system row-version column, which Npgsql's generator never emits (as in <c>CrmLeadConcurrencyToken</c>);
/// EF records it only so the model snapshot knows the token.</description></item>
/// <item><description>A-10: items gain <c>PendingApprovalId</c> and <c>SendCount</c> (round binding), then — M3, the
/// same mechanism as <c>CrmBriefBackfillPendingApprovalIds</c> — every item already <c>PendingApproval</c> is bound to
/// its open <c>approvals.approvals</c> row (<c>SubjectType</c> 3 = RequirementItem, <c>Decision IS NULL</c>, newest
/// wins) with <c>SendCount</c> 1, so its client's decision applies instead of being dropped. A documented one-off
/// cross-schema read in a migration; the <c>to_regclass</c> guard keeps CRM migratable alone. Pending items with no
/// open approval stay NULL — count them before deploying.</description></item>
/// </list>
///
/// <para><b>Guards first, before any DDL.</b> A change that could fail on existing data raises a clear error
/// instead, the same way <c>CrmBriefStageRequired</c> does: resolve the offending rows by hand, then
/// migrate.</para>
/// </summary>
public partial class CrmItemsPeriodsHardening : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            DO $$ BEGIN
              IF EXISTS (
                SELECT 1 FROM crm.crm_requirement_items
                WHERE "SupersedesItemId" IS NOT NULL
                GROUP BY "SupersedesItemId" HAVING COUNT(*) > 1)
              THEN RAISE EXCEPTION 'crm_requirement_items has an item superseded more than once — resolve before migrating';
              END IF;
            END $$;
            """);

        migrationBuilder.DropIndex(
            name: "IX_crm_requirement_items_SupersedesItemId",
            schema: "crm",
            table: "crm_requirement_items");

        migrationBuilder.AddColumn<uint>(
            name: "xmin",
            schema: "crm",
            table: "crm_requirement_periods",
            type: "xid",
            rowVersion: true,
            nullable: false,
            defaultValue: 0u);

        migrationBuilder.AddColumn<Guid>(
            name: "PendingApprovalId",
            schema: "crm",
            table: "crm_requirement_items",
            type: "uuid",
            nullable: true);

        migrationBuilder.AddColumn<int>(
            name: "SendCount",
            schema: "crm",
            table: "crm_requirement_items",
            type: "integer",
            nullable: false,
            defaultValue: 0);

        migrationBuilder.AddColumn<uint>(
            name: "xmin",
            schema: "crm",
            table: "crm_requirement_items",
            type: "xid",
            rowVersion: true,
            nullable: false,
            defaultValue: 0u);

        migrationBuilder.CreateIndex(
            name: "IX_crm_requirement_items_SupersedesItemId",
            schema: "crm",
            table: "crm_requirement_items",
            column: "SupersedesItemId",
            unique: true,
            filter: "\"SupersedesItemId\" IS NOT NULL");

        migrationBuilder.Sql("""
            DO $$ BEGIN
              IF to_regclass('approvals.approvals') IS NOT NULL THEN
                UPDATE crm.crm_requirement_items i
                SET "PendingApprovalId" = a."Id", "SendCount" = 1
                FROM (
                  SELECT DISTINCT ON ("SubjectId") "Id", "SubjectId"
                  FROM approvals.approvals
                  WHERE "SubjectType" = 3 AND "Decision" IS NULL
                  ORDER BY "SubjectId", "CreatedAtUtc" DESC) a
                WHERE a."SubjectId" = i."Id" AND i."ApprovalStatus" = 2 AND i."PendingApprovalId" IS NULL;
              END IF;
            END $$;
            """);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_crm_requirement_items_SupersedesItemId",
            schema: "crm",
            table: "crm_requirement_items");

        migrationBuilder.DropColumn(
            name: "xmin",
            schema: "crm",
            table: "crm_requirement_periods");

        migrationBuilder.DropColumn(
            name: "PendingApprovalId",
            schema: "crm",
            table: "crm_requirement_items");

        migrationBuilder.DropColumn(
            name: "SendCount",
            schema: "crm",
            table: "crm_requirement_items");

        migrationBuilder.DropColumn(
            name: "xmin",
            schema: "crm",
            table: "crm_requirement_items");

        migrationBuilder.CreateIndex(
            name: "IX_crm_requirement_items_SupersedesItemId",
            schema: "crm",
            table: "crm_requirement_items",
            column: "SupersedesItemId");
    }
}
