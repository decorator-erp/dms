using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Crm.Infrastructure.Persistence.Migrations.InitialCrm;

/// <inheritdoc />
public partial class CrmBriefRoundOutcome : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<DateTime>(
            name: "DecidedAtUtc",
            schema: "crm",
            table: "crm_brief_head_snapshots",
            type: "timestamp with time zone",
            nullable: true);

        migrationBuilder.AddColumn<int>(
            name: "Outcome",
            schema: "crm",
            table: "crm_brief_head_snapshots",
            type: "integer",
            nullable: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "DecidedAtUtc",
            schema: "crm",
            table: "crm_brief_head_snapshots");

        migrationBuilder.DropColumn(
            name: "Outcome",
            schema: "crm",
            table: "crm_brief_head_snapshots");
    }
}
