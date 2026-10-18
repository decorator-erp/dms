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

        migrationBuilder.DropColumn(
            name: "xmin",
            schema: "crm",
            table: "crm_brief_spaces");
    }
}
