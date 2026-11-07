using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Projects.Infrastructure.Persistence.Migrations.InitialProjects;

/// <inheritdoc />
public partial class ProjectEnumCheckConstraints : Migration
{
    // NP4 (C3): an enum column holding a value outside its enum would fail the new CHECK; refuse loudly with the
    // counts per column instead of rewriting anything. Fix the rows by hand, then re-run.
    private const string InvalidEnumGuard = """
        DO $$
        DECLARE
            n bigint;
            invalid bigint := 0;
            detail text := '';
        BEGIN
        SELECT count(*) INTO n FROM projects.projects WHERE "Status" NOT BETWEEN 1 AND 5;
        IF n > 0 THEN
            invalid := invalid + n;
            detail := detail || format(' projects.Status=%s', n);
        END IF;
        SELECT count(*) INTO n FROM projects.projects WHERE "WorkflowType" NOT BETWEEN 1 AND 2;
        IF n > 0 THEN
            invalid := invalid + n;
            detail := detail || format(' projects.WorkflowType=%s', n);
        END IF;
        SELECT count(*) INTO n FROM projects.project_tasks WHERE "Status" NOT BETWEEN 1 AND 7;
        IF n > 0 THEN
            invalid := invalid + n;
            detail := detail || format(' project_tasks.Status=%s', n);
        END IF;
        SELECT count(*) INTO n FROM projects.project_tasks WHERE "Priority" NOT BETWEEN 1 AND 4;
        IF n > 0 THEN
            invalid := invalid + n;
            detail := detail || format(' project_tasks.Priority=%s', n);
        END IF;
        SELECT count(*) INTO n FROM projects.project_stages WHERE "Status" NOT BETWEEN 1 AND 4;
        IF n > 0 THEN
            invalid := invalid + n;
            detail := detail || format(' project_stages.Status=%s', n);
        END IF;
        SELECT count(*) INTO n FROM projects.project_members WHERE "Access" NOT BETWEEN 1 AND 3;
        IF n > 0 THEN
            invalid := invalid + n;
            detail := detail || format(' project_members.Access=%s', n);
        END IF;
        SELECT count(*) INTO n FROM projects.stage_team_members WHERE "Role" NOT BETWEEN 1 AND 4;
        IF n > 0 THEN
            invalid := invalid + n;
            detail := detail || format(' stage_team_members.Role=%s', n);
        END IF;
        SELECT count(*) INTO n FROM projects.stage_templates WHERE "Type" NOT BETWEEN 1 AND 2;
        IF n > 0 THEN
            invalid := invalid + n;
            detail := detail || format(' stage_templates.Type=%s', n);
        END IF;
        SELECT count(*) INTO n FROM projects.scopes_of_work WHERE "Status" NOT BETWEEN 1 AND 2;
        IF n > 0 THEN
            invalid := invalid + n;
            detail := detail || format(' scopes_of_work.Status=%s', n);
        END IF;
        SELECT count(*) INTO n FROM projects.gate_conditions WHERE "ConditionType" NOT BETWEEN 1 AND 10;
        IF n > 0 THEN
            invalid := invalid + n;
            detail := detail || format(' gate_conditions.ConditionType=%s', n);
        END IF;
        SELECT count(*) INTO n FROM projects.gate_conditions WHERE "Trigger" NOT BETWEEN 1 AND 2;
        IF n > 0 THEN
            invalid := invalid + n;
            detail := detail || format(' gate_conditions.Trigger=%s', n);
        END IF;
        SELECT count(*) INTO n FROM projects.gate_conditions WHERE "ApprovalType" NOT BETWEEN 1 AND 2;
        IF n > 0 THEN
            invalid := invalid + n;
            detail := detail || format(' gate_conditions.ApprovalType=%s', n);
        END IF;
        SELECT count(*) INTO n FROM projects.gate_conditions WHERE "Status" NOT BETWEEN 1 AND 3;
        IF n > 0 THEN
            invalid := invalid + n;
            detail := detail || format(' gate_conditions.Status=%s', n);
        END IF;
            IF invalid > 0 THEN
                RAISE EXCEPTION '% invalid rows: enum values out of range:%', invalid, detail;
            END IF;
        END $$;
        """;

    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(InvalidEnumGuard);

        migrationBuilder.AddCheckConstraint(
            name: "CK_stage_templates_Type",
            schema: "projects",
            table: "stage_templates",
            sql: "\"Type\" BETWEEN 1 AND 2");

        migrationBuilder.AddCheckConstraint(
            name: "CK_stage_team_members_Role",
            schema: "projects",
            table: "stage_team_members",
            sql: "\"Role\" BETWEEN 1 AND 4");

        migrationBuilder.AddCheckConstraint(
            name: "CK_scopes_of_work_Status",
            schema: "projects",
            table: "scopes_of_work",
            sql: "\"Status\" BETWEEN 1 AND 2");

        migrationBuilder.AddCheckConstraint(
            name: "CK_projects_Status",
            schema: "projects",
            table: "projects",
            sql: "\"Status\" BETWEEN 1 AND 5");

        migrationBuilder.AddCheckConstraint(
            name: "CK_projects_WorkflowType",
            schema: "projects",
            table: "projects",
            sql: "\"WorkflowType\" BETWEEN 1 AND 2");

        migrationBuilder.AddCheckConstraint(
            name: "CK_project_tasks_Priority",
            schema: "projects",
            table: "project_tasks",
            sql: "\"Priority\" BETWEEN 1 AND 4");

        migrationBuilder.AddCheckConstraint(
            name: "CK_project_tasks_Status",
            schema: "projects",
            table: "project_tasks",
            sql: "\"Status\" BETWEEN 1 AND 7");

        migrationBuilder.AddCheckConstraint(
            name: "CK_project_stages_Status",
            schema: "projects",
            table: "project_stages",
            sql: "\"Status\" BETWEEN 1 AND 4");

        migrationBuilder.AddCheckConstraint(
            name: "CK_project_members_Access",
            schema: "projects",
            table: "project_members",
            sql: "\"Access\" BETWEEN 1 AND 3");

        migrationBuilder.AddCheckConstraint(
            name: "CK_gate_conditions_ApprovalType",
            schema: "projects",
            table: "gate_conditions",
            sql: "\"ApprovalType\" BETWEEN 1 AND 2");

        migrationBuilder.AddCheckConstraint(
            name: "CK_gate_conditions_ConditionType",
            schema: "projects",
            table: "gate_conditions",
            sql: "\"ConditionType\" BETWEEN 1 AND 10");

        migrationBuilder.AddCheckConstraint(
            name: "CK_gate_conditions_Status",
            schema: "projects",
            table: "gate_conditions",
            sql: "\"Status\" BETWEEN 1 AND 3");

        migrationBuilder.AddCheckConstraint(
            name: "CK_gate_conditions_Trigger",
            schema: "projects",
            table: "gate_conditions",
            sql: "\"Trigger\" BETWEEN 1 AND 2");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropCheckConstraint(
            name: "CK_stage_templates_Type",
            schema: "projects",
            table: "stage_templates");

        migrationBuilder.DropCheckConstraint(
            name: "CK_stage_team_members_Role",
            schema: "projects",
            table: "stage_team_members");

        migrationBuilder.DropCheckConstraint(
            name: "CK_scopes_of_work_Status",
            schema: "projects",
            table: "scopes_of_work");

        migrationBuilder.DropCheckConstraint(
            name: "CK_projects_Status",
            schema: "projects",
            table: "projects");

        migrationBuilder.DropCheckConstraint(
            name: "CK_projects_WorkflowType",
            schema: "projects",
            table: "projects");

        migrationBuilder.DropCheckConstraint(
            name: "CK_project_tasks_Priority",
            schema: "projects",
            table: "project_tasks");

        migrationBuilder.DropCheckConstraint(
            name: "CK_project_tasks_Status",
            schema: "projects",
            table: "project_tasks");

        migrationBuilder.DropCheckConstraint(
            name: "CK_project_stages_Status",
            schema: "projects",
            table: "project_stages");

        migrationBuilder.DropCheckConstraint(
            name: "CK_project_members_Access",
            schema: "projects",
            table: "project_members");

        migrationBuilder.DropCheckConstraint(
            name: "CK_gate_conditions_ApprovalType",
            schema: "projects",
            table: "gate_conditions");

        migrationBuilder.DropCheckConstraint(
            name: "CK_gate_conditions_ConditionType",
            schema: "projects",
            table: "gate_conditions");

        migrationBuilder.DropCheckConstraint(
            name: "CK_gate_conditions_Status",
            schema: "projects",
            table: "gate_conditions");

        migrationBuilder.DropCheckConstraint(
            name: "CK_gate_conditions_Trigger",
            schema: "projects",
            table: "gate_conditions");
    }
}
