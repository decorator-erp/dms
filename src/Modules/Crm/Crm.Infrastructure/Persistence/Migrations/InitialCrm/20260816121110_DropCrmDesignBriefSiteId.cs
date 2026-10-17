using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Crm.Infrastructure.Persistence.Migrations.InitialCrm;

/// <inheritdoc />
public partial class DropCrmDesignBriefSiteId : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.DropColumn(
            name: "SiteId",
            schema: "crm",
            table: "crm_design_briefs");

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.AddColumn<Guid>(
            name: "SiteId",
            schema: "crm",
            table: "crm_design_briefs",
            type: "uuid",
            nullable: true);
}
