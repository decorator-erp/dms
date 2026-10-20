using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Crm.Infrastructure.Persistence.Migrations.InitialCrm;

/// <inheritdoc />
public partial class CrmBriefApprovalRounds : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<Guid>(
            name: "PendingApprovalId",
            schema: "crm",
            table: "crm_design_briefs",
            type: "uuid",
            nullable: true);

        // No-op in SQL: xmin is Postgres' system row-version column, which every table already has.
        // Npgsql's migration SQL generator skips system columns, so this emits no DDL (checked with
        // `dotnet ef migrations script`); EF records it only so the model snapshot knows the token.
        migrationBuilder.AddColumn<uint>(
            name: "xmin",
            schema: "crm",
            table: "crm_design_briefs",
            type: "xid",
            rowVersion: true,
            nullable: false,
            defaultValue: 0u);

        migrationBuilder.AddColumn<Guid>(
            name: "PendingApprovalId",
            schema: "crm",
            table: "crm_brief_spaces",
            type: "uuid",
            nullable: true);

        migrationBuilder.AddColumn<int>(
            name: "SendCount",
            schema: "crm",
            table: "crm_brief_spaces",
            type: "integer",
            nullable: false,
            defaultValue: 0);

        // No-op in SQL: xmin is Postgres' system row-version column, which every table already has.
        // Npgsql's migration SQL generator skips system columns, so this emits no DDL (checked with
        // `dotnet ef migrations script`); EF records it only so the model snapshot knows the token.
        migrationBuilder.AddColumn<uint>(
            name: "xmin",
            schema: "crm",
            table: "crm_brief_spaces",
            type: "xid",
            rowVersion: true,
            nullable: false,
            defaultValue: 0u);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "PendingApprovalId",
            schema: "crm",
            table: "crm_design_briefs");

        // No-op in SQL, like the AddColumn in Up: Npgsql never drops the xmin system column.
        migrationBuilder.DropColumn(
            name: "xmin",
            schema: "crm",
            table: "crm_design_briefs");

        migrationBuilder.DropColumn(
            name: "PendingApprovalId",
            schema: "crm",
            table: "crm_brief_spaces");

        migrationBuilder.DropColumn(
            name: "SendCount",
            schema: "crm",
            table: "crm_brief_spaces");

        // No-op in SQL, like the AddColumn in Up: Npgsql never drops the xmin system column.
        migrationBuilder.DropColumn(
            name: "xmin",
            schema: "crm",
            table: "crm_brief_spaces");
    }
}
