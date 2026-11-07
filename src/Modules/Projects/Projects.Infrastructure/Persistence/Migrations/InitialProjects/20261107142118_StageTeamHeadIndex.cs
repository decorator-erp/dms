using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Projects.Infrastructure.Persistence.Migrations.InitialProjects;

/// <summary>R1-12: <c>stage_teams."HeadId"</c> leads an index — every project access check reads it (a team head is
/// Manager on the project), and so do its foreign key's checks against <c>hr.employees</c>. No guard: an index on
/// existing rows cannot fail.</summary>
public partial class StageTeamHeadIndex : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.CreateIndex(
            name: "IX_stage_teams_HeadId",
            schema: "projects",
            table: "stage_teams",
            column: "HeadId");

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.DropIndex(
            name: "IX_stage_teams_HeadId",
            schema: "projects",
            table: "stage_teams");
}
