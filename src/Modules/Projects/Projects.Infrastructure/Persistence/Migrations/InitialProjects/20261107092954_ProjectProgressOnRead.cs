using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Projects.Infrastructure.Persistence.Migrations.InitialProjects;

/// <inheritdoc />
public partial class ProjectProgressOnRead : Migration
{
    // P2/NP5 (C1/C2). The project's progress is computed on read from the stage counters, so the stored column
    // goes. The stage counters were never updated by task transitions before this fix, so every existing stage is
    // recounted once from its task rows with the same rule StageCounters applies from now on (Cancelled tasks
    // excluded from the total). Derived values only: no row is removed or invalid, so no guard is needed.
    private const string RecountStages = """
        UPDATE projects.project_stages AS s
        SET "TotalTasks" = c.total,
            "CompletedTasks" = c.done,
            "ProgressPercent" = CASE WHEN c.total = 0 THEN 0 ELSE round(100.0 * c.done / c.total, 2) END
        FROM (
            SELECT st."Id",
                   count(t."Id") FILTER (WHERE t."Status" <> 7) AS total, -- 7 = Cancelled
                   count(t."Id") FILTER (WHERE t."Status" = 4) AS done -- 4 = Completed
            FROM projects.project_stages AS st
            LEFT JOIN projects.project_tasks AS t ON t."ProjectStageId" = st."Id"
            GROUP BY st."Id"
        ) AS c
        WHERE s."Id" = c."Id";
        """;

    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "ProgressPercentage",
            schema: "projects",
            table: "projects");

        migrationBuilder.Sql(RecountStages);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.AddColumn<decimal>(
            name: "ProgressPercentage",
            schema: "projects",
            table: "projects",
            type: "numeric(5,2)",
            precision: 5,
            scale: 2,
            nullable: false,
            defaultValue: 0m);
}
