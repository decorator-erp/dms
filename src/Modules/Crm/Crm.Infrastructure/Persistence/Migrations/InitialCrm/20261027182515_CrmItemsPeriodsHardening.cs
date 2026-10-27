using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Crm.Infrastructure.Persistence.Migrations.InitialCrm;

/// <summary>
/// PR A of the CRM backend completion (items, periods, spaces). One additive migration:
/// <list type="bullet">
/// <item><description>A-6: <c>SupersedesItemId</c> becomes unique where set — the item ledger cannot fork.</description></item>
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

        migrationBuilder.CreateIndex(
            name: "IX_crm_requirement_items_SupersedesItemId",
            schema: "crm",
            table: "crm_requirement_items",
            column: "SupersedesItemId",
            unique: true,
            filter: "\"SupersedesItemId\" IS NOT NULL");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_crm_requirement_items_SupersedesItemId",
            schema: "crm",
            table: "crm_requirement_items");

        migrationBuilder.CreateIndex(
            name: "IX_crm_requirement_items_SupersedesItemId",
            schema: "crm",
            table: "crm_requirement_items",
            column: "SupersedesItemId");
    }
}
