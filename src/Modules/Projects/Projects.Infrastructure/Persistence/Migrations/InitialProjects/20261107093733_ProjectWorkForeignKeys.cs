using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Projects.Infrastructure.Persistence.Migrations.InitialProjects;

/// <inheritdoc />
public partial class ProjectWorkForeignKeys : Migration
{
    // P1 (C3): the old project delete removed only the project row, so a stage or task may already name a project
    // that is gone. The foreign keys would fail on such a row; refuse loudly with the counts instead of deleting
    // anything silently. Fix the rows by hand, then re-run.
    private const string OrphanGuard = """
        DO $$
        DECLARE
            orphan_stages bigint;
            orphan_tasks bigint;
        BEGIN
            SELECT count(*) INTO orphan_stages FROM projects.project_stages AS s
            WHERE NOT EXISTS (SELECT 1 FROM projects.projects AS p WHERE p."Id" = s."ProjectId");
            SELECT count(*) INTO orphan_tasks FROM projects.project_tasks AS t
            WHERE NOT EXISTS (SELECT 1 FROM projects.projects AS p WHERE p."Id" = t."ProjectId");
            IF orphan_stages + orphan_tasks > 0 THEN
                RAISE EXCEPTION '% orphan rows: % project_stages and % project_tasks name a project that does not exist',
                    orphan_stages + orphan_tasks, orphan_stages, orphan_tasks;
            END IF;
        END $$;
        """;

    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(OrphanGuard);

        migrationBuilder.AddForeignKey(
            name: "FK_project_stages_projects_ProjectId",
            schema: "projects",
            table: "project_stages",
            column: "ProjectId",
            principalSchema: "projects",
            principalTable: "projects",
            principalColumn: "Id",
            onDelete: ReferentialAction.Restrict);

        migrationBuilder.AddForeignKey(
            name: "FK_project_tasks_projects_ProjectId",
            schema: "projects",
            table: "project_tasks",
            column: "ProjectId",
            principalSchema: "projects",
            principalTable: "projects",
            principalColumn: "Id",
            onDelete: ReferentialAction.Restrict);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropForeignKey(
            name: "FK_project_stages_projects_ProjectId",
            schema: "projects",
            table: "project_stages");

        migrationBuilder.DropForeignKey(
            name: "FK_project_tasks_projects_ProjectId",
            schema: "projects",
            table: "project_tasks");
    }
}
