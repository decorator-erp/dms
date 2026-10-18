using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Crm.Infrastructure.Persistence.Migrations.InitialCrm;

/// <summary>
/// M1 + M2 of the CRM brief fixes: <c>crm_design_briefs.ProjectStageId</c> becomes NOT NULL (one brief per
/// stage — NULL stages were never unique in Postgres and never satisfied a gate), and
/// <c>crm_brief_spaces.SupersedesSpaceId</c> becomes unique where set (the ledger cannot fork).
///
/// <para><b>Both start with a data guard, before any DDL.</b> Nobody in the cloud fix session could see
/// a real database, so instead of assuming the data is clean the migration refuses to run on data that
/// isn't: resolve the offending rows by hand, then migrate. There is no automatic backfill on purpose —
/// which stage a stage-less brief belongs to, and which of two corrections is the real one, are business
/// calls.</para>
/// </summary>
public partial class CrmBriefStageRequired : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            DO $$ BEGIN
              IF EXISTS (SELECT 1 FROM crm.crm_design_briefs WHERE "ProjectStageId" IS NULL)
              THEN RAISE EXCEPTION 'crm_design_briefs has rows with NULL ProjectStageId — resolve before migrating';
              END IF;
            END $$;
            """);

        migrationBuilder.Sql("""
            DO $$ BEGIN
              IF EXISTS (
                SELECT 1 FROM crm.crm_brief_spaces
                WHERE "SupersedesSpaceId" IS NOT NULL
                GROUP BY "SupersedesSpaceId" HAVING COUNT(*) > 1)
              THEN RAISE EXCEPTION 'crm_brief_spaces has an entry superseded more than once — resolve before migrating';
              END IF;
            END $$;
            """);

        migrationBuilder.DropIndex(
            name: "IX_crm_design_briefs_ProjectStageId",
            schema: "crm",
            table: "crm_design_briefs");

        migrationBuilder.DropIndex(
            name: "IX_crm_brief_spaces_SupersedesSpaceId",
            schema: "crm",
            table: "crm_brief_spaces");

        migrationBuilder.AlterColumn<Guid>(
            name: "ProjectStageId",
            schema: "crm",
            table: "crm_design_briefs",
            type: "uuid",
            nullable: false,
            oldClrType: typeof(Guid),
            oldType: "uuid",
            oldNullable: true);

        migrationBuilder.CreateIndex(
            name: "IX_crm_design_briefs_ProjectStageId",
            schema: "crm",
            table: "crm_design_briefs",
            column: "ProjectStageId");

        migrationBuilder.CreateIndex(
            name: "IX_crm_brief_spaces_SupersedesSpaceId",
            schema: "crm",
            table: "crm_brief_spaces",
            column: "SupersedesSpaceId",
            unique: true,
            filter: "\"SupersedesSpaceId\" IS NOT NULL");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_crm_design_briefs_ProjectStageId",
            schema: "crm",
            table: "crm_design_briefs");

        migrationBuilder.DropIndex(
            name: "IX_crm_brief_spaces_SupersedesSpaceId",
            schema: "crm",
            table: "crm_brief_spaces");

        migrationBuilder.AlterColumn<Guid>(
            name: "ProjectStageId",
            schema: "crm",
            table: "crm_design_briefs",
            type: "uuid",
            nullable: true,
            oldClrType: typeof(Guid),
            oldType: "uuid");

        migrationBuilder.CreateIndex(
            name: "IX_crm_design_briefs_ProjectStageId",
            schema: "crm",
            table: "crm_design_briefs",
            column: "ProjectStageId",
            filter: "\"ProjectStageId\" IS NOT NULL");

        migrationBuilder.CreateIndex(
            name: "IX_crm_brief_spaces_SupersedesSpaceId",
            schema: "crm",
            table: "crm_brief_spaces",
            column: "SupersedesSpaceId");
    }
}
