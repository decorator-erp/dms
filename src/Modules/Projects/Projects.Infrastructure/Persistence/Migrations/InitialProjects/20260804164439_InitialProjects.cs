using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Projects.Infrastructure.Persistence.Migrations.InitialProjects;

/// <inheritdoc />
public partial class InitialProjects : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.EnsureSchema(
            name: "projects");

        migrationBuilder.CreateTable(
            name: "gate_conditions",
            schema: "projects",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                ProjectStageId = table.Column<Guid>(type: "uuid", nullable: true),
                ProjectTaskId = table.Column<Guid>(type: "uuid", nullable: true),
                ConditionType = table.Column<int>(type: "integer", nullable: false),
                Trigger = table.Column<int>(type: "integer", nullable: false),
                Label = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                ReferencedTaskId = table.Column<Guid>(type: "uuid", nullable: true),
                ReferencedStageId = table.Column<Guid>(type: "uuid", nullable: true),
                ApprovalType = table.Column<int>(type: "integer", nullable: true),
                Status = table.Column<int>(type: "integer", nullable: false),
                ResolvedById = table.Column<Guid>(type: "uuid", nullable: true),
                ResolvedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                ResolutionNote = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                ApprovalId = table.Column<Guid>(type: "uuid", nullable: true),
                IsActive = table.Column<bool>(type: "boolean", nullable: false),
                CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
            },
            constraints: table => table.PrimaryKey("PK_gate_conditions", x => x.Id));

        migrationBuilder.CreateTable(
            name: "project_stages",
            schema: "projects",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                StageId = table.Column<Guid>(type: "uuid", nullable: true),
                Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                Description = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                Order = table.Column<int>(type: "integer", nullable: false),
                Status = table.Column<int>(type: "integer", nullable: false),
                ProgressPercent = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: false),
                CompletedTasks = table.Column<int>(type: "integer", nullable: false),
                TotalTasks = table.Column<int>(type: "integer", nullable: false),
                Cost = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                EstimatedStartDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                EstimatedEndDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                ActualStartDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                ActualEndDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                CompletedById = table.Column<Guid>(type: "uuid", nullable: true),
                Notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                IsActive = table.Column<bool>(type: "boolean", nullable: false),
                CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
            },
            constraints: table => table.PrimaryKey("PK_project_stages", x => x.Id));

        migrationBuilder.CreateTable(
            name: "project_sub_tasks",
            schema: "projects",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                ProjectTaskId = table.Column<Guid>(type: "uuid", nullable: false),
                Title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                Description = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                Order = table.Column<int>(type: "integer", nullable: false),
                IsRequired = table.Column<bool>(type: "boolean", nullable: false),
                IsDone = table.Column<bool>(type: "boolean", nullable: false),
                CompletedById = table.Column<Guid>(type: "uuid", nullable: true),
                CompletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                IsActive = table.Column<bool>(type: "boolean", nullable: false),
                CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
            },
            constraints: table => table.PrimaryKey("PK_project_sub_tasks", x => x.Id));

        migrationBuilder.CreateTable(
            name: "project_tasks",
            schema: "projects",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                ProjectStageId = table.Column<Guid>(type: "uuid", nullable: true),
                StageTaskId = table.Column<Guid>(type: "uuid", nullable: true),
                Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                Description = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                Order = table.Column<int>(type: "integer", nullable: false),
                Status = table.Column<int>(type: "integer", nullable: false),
                Priority = table.Column<int>(type: "integer", nullable: false),
                Weight = table.Column<decimal>(type: "numeric(9,2)", precision: 9, scale: 2, nullable: false),
                Notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                DueDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                StartedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                CompletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                AssignedToId = table.Column<Guid>(type: "uuid", nullable: true),
                DepartmentId = table.Column<Guid>(type: "uuid", nullable: true),
                Cost = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                TotalSubTasks = table.Column<int>(type: "integer", nullable: false),
                CompletedSubTasks = table.Column<int>(type: "integer", nullable: false),
                IsActive = table.Column<bool>(type: "boolean", nullable: false),
                CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
            },
            constraints: table => table.PrimaryKey("PK_project_tasks", x => x.Id));

        migrationBuilder.CreateTable(
            name: "projects",
            schema: "projects",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                Code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                Description = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                Status = table.Column<int>(type: "integer", nullable: false),
                WorkflowType = table.Column<int>(type: "integer", nullable: false),
                Address = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                Notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                ProgressPercentage = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: false),
                EstimatedStartDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                EstimatedEndDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                ActualStartDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                ActualEndDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                ClientId = table.Column<Guid>(type: "uuid", nullable: false),
                OwnerId = table.Column<Guid>(type: "uuid", nullable: false),
                IsActive = table.Column<bool>(type: "boolean", nullable: false),
                CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
            },
            constraints: table => table.PrimaryKey("PK_projects", x => x.Id));

        migrationBuilder.CreateTable(
            name: "stage_sub_tasks",
            schema: "projects",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                StageTaskId = table.Column<Guid>(type: "uuid", nullable: false),
                Title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                Description = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                Order = table.Column<int>(type: "integer", nullable: false),
                IsRequired = table.Column<bool>(type: "boolean", nullable: false),
                IsActive = table.Column<bool>(type: "boolean", nullable: false),
                CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
            },
            constraints: table => table.PrimaryKey("PK_stage_sub_tasks", x => x.Id));

        migrationBuilder.CreateTable(
            name: "stage_tasks",
            schema: "projects",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                StageId = table.Column<Guid>(type: "uuid", nullable: false),
                Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                Description = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                Order = table.Column<int>(type: "integer", nullable: false),
                Weight = table.Column<decimal>(type: "numeric(9,2)", precision: 9, scale: 2, nullable: false),
                DepartmentId = table.Column<Guid>(type: "uuid", nullable: true),
                IsActive = table.Column<bool>(type: "boolean", nullable: false),
                CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
            },
            constraints: table => table.PrimaryKey("PK_stage_tasks", x => x.Id));

        migrationBuilder.CreateTable(
            name: "stage_teams",
            schema: "projects",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                ProjectStageId = table.Column<Guid>(type: "uuid", nullable: false),
                DepartmentId = table.Column<Guid>(type: "uuid", nullable: true),
                HeadId = table.Column<Guid>(type: "uuid", nullable: false),
                IsActive = table.Column<bool>(type: "boolean", nullable: false),
                CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
            },
            constraints: table => table.PrimaryKey("PK_stage_teams", x => x.Id));

        migrationBuilder.CreateTable(
            name: "stage_templates",
            schema: "projects",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                Description = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                Type = table.Column<int>(type: "integer", nullable: false),
                IsDefault = table.Column<bool>(type: "boolean", nullable: false),
                IsActive = table.Column<bool>(type: "boolean", nullable: false),
                CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
            },
            constraints: table => table.PrimaryKey("PK_stage_templates", x => x.Id));

        migrationBuilder.CreateTable(
            name: "stages",
            schema: "projects",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                StageTemplateId = table.Column<Guid>(type: "uuid", nullable: false),
                Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                Description = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                Order = table.Column<int>(type: "integer", nullable: false),
                WeightPercent = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: false),
                IsActive = table.Column<bool>(type: "boolean", nullable: false),
                CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
            },
            constraints: table => table.PrimaryKey("PK_stages", x => x.Id));

        migrationBuilder.CreateTable(
            name: "task_comments",
            schema: "projects",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                TaskId = table.Column<Guid>(type: "uuid", nullable: false),
                EmployeeId = table.Column<Guid>(type: "uuid", nullable: false),
                Content = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                IsActive = table.Column<bool>(type: "boolean", nullable: false),
                CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
            },
            constraints: table => table.PrimaryKey("PK_task_comments", x => x.Id));

        migrationBuilder.CreateTable(
            name: "stage_team_members",
            schema: "projects",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                StageTeamId = table.Column<Guid>(type: "uuid", nullable: false),
                EmployeeId = table.Column<Guid>(type: "uuid", nullable: false),
                Role = table.Column<int>(type: "integer", nullable: false),
                IsActive = table.Column<bool>(type: "boolean", nullable: false),
                CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_stage_team_members", x => x.Id);
                table.ForeignKey(
                    name: "FK_stage_team_members_stage_teams_StageTeamId",
                    column: x => x.StageTeamId,
                    principalSchema: "projects",
                    principalTable: "stage_teams",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "stage_departments",
            schema: "projects",
            columns: table => new
            {
                StageId = table.Column<Guid>(type: "uuid", nullable: false),
                DepartmentId = table.Column<Guid>(type: "uuid", nullable: false),
                IsActive = table.Column<bool>(type: "boolean", nullable: false),
                CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_stage_departments", x => new { x.StageId, x.DepartmentId });
                table.ForeignKey(
                    name: "FK_stage_departments_stages_StageId",
                    column: x => x.StageId,
                    principalSchema: "projects",
                    principalTable: "stages",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_gate_conditions_ApprovalId",
            schema: "projects",
            table: "gate_conditions",
            column: "ApprovalId");

        migrationBuilder.CreateIndex(
            name: "IX_gate_conditions_ProjectStageId",
            schema: "projects",
            table: "gate_conditions",
            column: "ProjectStageId");

        migrationBuilder.CreateIndex(
            name: "IX_gate_conditions_ProjectTaskId",
            schema: "projects",
            table: "gate_conditions",
            column: "ProjectTaskId");

        migrationBuilder.CreateIndex(
            name: "IX_project_stages_CompletedById",
            schema: "projects",
            table: "project_stages",
            column: "CompletedById");

        migrationBuilder.CreateIndex(
            name: "IX_project_stages_ProjectId",
            schema: "projects",
            table: "project_stages",
            column: "ProjectId");

        migrationBuilder.CreateIndex(
            name: "IX_project_sub_tasks_ProjectTaskId",
            schema: "projects",
            table: "project_sub_tasks",
            column: "ProjectTaskId");

        migrationBuilder.CreateIndex(
            name: "IX_project_tasks_AssignedToId",
            schema: "projects",
            table: "project_tasks",
            column: "AssignedToId");

        migrationBuilder.CreateIndex(
            name: "IX_project_tasks_DepartmentId",
            schema: "projects",
            table: "project_tasks",
            column: "DepartmentId");

        migrationBuilder.CreateIndex(
            name: "IX_project_tasks_ProjectId",
            schema: "projects",
            table: "project_tasks",
            column: "ProjectId");

        migrationBuilder.CreateIndex(
            name: "IX_project_tasks_ProjectStageId",
            schema: "projects",
            table: "project_tasks",
            column: "ProjectStageId");

        migrationBuilder.CreateIndex(
            name: "IX_projects_Code",
            schema: "projects",
            table: "projects",
            column: "Code",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_projects_OwnerId",
            schema: "projects",
            table: "projects",
            column: "OwnerId");

        migrationBuilder.CreateIndex(
            name: "IX_stage_departments_DepartmentId",
            schema: "projects",
            table: "stage_departments",
            column: "DepartmentId");

        migrationBuilder.CreateIndex(
            name: "IX_stage_sub_tasks_StageTaskId",
            schema: "projects",
            table: "stage_sub_tasks",
            column: "StageTaskId");

        migrationBuilder.CreateIndex(
            name: "IX_stage_tasks_DepartmentId",
            schema: "projects",
            table: "stage_tasks",
            column: "DepartmentId");

        migrationBuilder.CreateIndex(
            name: "IX_stage_tasks_StageId",
            schema: "projects",
            table: "stage_tasks",
            column: "StageId");

        migrationBuilder.CreateIndex(
            name: "IX_stage_team_members_EmployeeId",
            schema: "projects",
            table: "stage_team_members",
            column: "EmployeeId");

        migrationBuilder.CreateIndex(
            name: "IX_stage_team_members_StageTeamId",
            schema: "projects",
            table: "stage_team_members",
            column: "StageTeamId");

        migrationBuilder.CreateIndex(
            name: "IX_stage_teams_ProjectStageId",
            schema: "projects",
            table: "stage_teams",
            column: "ProjectStageId");

        migrationBuilder.CreateIndex(
            name: "IX_stage_templates_Name",
            schema: "projects",
            table: "stage_templates",
            column: "Name",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_stages_StageTemplateId",
            schema: "projects",
            table: "stages",
            column: "StageTemplateId");

        migrationBuilder.CreateIndex(
            name: "IX_task_comments_EmployeeId",
            schema: "projects",
            table: "task_comments",
            column: "EmployeeId");

        migrationBuilder.CreateIndex(
            name: "IX_task_comments_TaskId",
            schema: "projects",
            table: "task_comments",
            column: "TaskId");

        // Cross-schema FKs - EF cannot emit these itself (identity/hr/approvals and projects are in
        // different EF models), so they're written by hand, same pattern as hr.employees' FK to
        // identity.users. ON DELETE RESTRICT, never CASCADE, across a module boundary (see
        // docs/cross-schema-foreign-keys.md). Requires hr.employees/hr.departments/approvals.approvals
        // to already exist, which is why ModuleDependencyOrder.SchemaOrder migrates "hr" and
        // "approvals" before "projects". Column names are quoted PascalCase, matching every other
        // cross-schema FK in this solution. projects.ClientId gets NO FK - CRM absent, documented here
        // and in docs/cross-schema-foreign-keys.md.
        migrationBuilder.Sql(
            """
            ALTER TABLE projects.projects
                ADD CONSTRAINT fk_projects_owner
                FOREIGN KEY ("OwnerId") REFERENCES hr.employees("Id")
                ON DELETE RESTRICT;

            ALTER TABLE projects.project_tasks
                ADD CONSTRAINT fk_project_tasks_assigned_to
                FOREIGN KEY ("AssignedToId") REFERENCES hr.employees("Id")
                ON DELETE RESTRICT;

            ALTER TABLE projects.project_tasks
                ADD CONSTRAINT fk_project_tasks_department
                FOREIGN KEY ("DepartmentId") REFERENCES hr.departments("Id")
                ON DELETE RESTRICT;

            ALTER TABLE projects.project_stages
                ADD CONSTRAINT fk_project_stages_completed_by
                FOREIGN KEY ("CompletedById") REFERENCES hr.employees("Id")
                ON DELETE RESTRICT;

            ALTER TABLE projects.project_sub_tasks
                ADD CONSTRAINT fk_project_sub_tasks_completed_by
                FOREIGN KEY ("CompletedById") REFERENCES hr.employees("Id")
                ON DELETE RESTRICT;

            ALTER TABLE projects.task_comments
                ADD CONSTRAINT fk_task_comments_employee
                FOREIGN KEY ("EmployeeId") REFERENCES hr.employees("Id")
                ON DELETE RESTRICT;

            ALTER TABLE projects.stage_teams
                ADD CONSTRAINT fk_stage_teams_head
                FOREIGN KEY ("HeadId") REFERENCES hr.employees("Id")
                ON DELETE RESTRICT;

            ALTER TABLE projects.stage_teams
                ADD CONSTRAINT fk_stage_teams_department
                FOREIGN KEY ("DepartmentId") REFERENCES hr.departments("Id")
                ON DELETE RESTRICT;

            ALTER TABLE projects.stage_team_members
                ADD CONSTRAINT fk_stage_team_members_employee
                FOREIGN KEY ("EmployeeId") REFERENCES hr.employees("Id")
                ON DELETE RESTRICT;

            ALTER TABLE projects.stage_tasks
                ADD CONSTRAINT fk_stage_tasks_department
                FOREIGN KEY ("DepartmentId") REFERENCES hr.departments("Id")
                ON DELETE RESTRICT;

            ALTER TABLE projects.stage_departments
                ADD CONSTRAINT fk_stage_departments_department
                FOREIGN KEY ("DepartmentId") REFERENCES hr.departments("Id")
                ON DELETE RESTRICT;

            ALTER TABLE projects.gate_conditions
                ADD CONSTRAINT fk_gate_conditions_resolved_by
                FOREIGN KEY ("ResolvedById") REFERENCES hr.employees("Id")
                ON DELETE RESTRICT;

            ALTER TABLE projects.gate_conditions
                ADD CONSTRAINT fk_gate_conditions_approval
                FOREIGN KEY ("ApprovalId") REFERENCES approvals.approvals("Id")
                ON DELETE RESTRICT;
            """);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            ALTER TABLE projects.gate_conditions DROP CONSTRAINT fk_gate_conditions_approval;
            ALTER TABLE projects.gate_conditions DROP CONSTRAINT fk_gate_conditions_resolved_by;
            ALTER TABLE projects.stage_departments DROP CONSTRAINT fk_stage_departments_department;
            ALTER TABLE projects.stage_tasks DROP CONSTRAINT fk_stage_tasks_department;
            ALTER TABLE projects.stage_team_members DROP CONSTRAINT fk_stage_team_members_employee;
            ALTER TABLE projects.stage_teams DROP CONSTRAINT fk_stage_teams_department;
            ALTER TABLE projects.stage_teams DROP CONSTRAINT fk_stage_teams_head;
            ALTER TABLE projects.task_comments DROP CONSTRAINT fk_task_comments_employee;
            ALTER TABLE projects.project_sub_tasks DROP CONSTRAINT fk_project_sub_tasks_completed_by;
            ALTER TABLE projects.project_stages DROP CONSTRAINT fk_project_stages_completed_by;
            ALTER TABLE projects.project_tasks DROP CONSTRAINT fk_project_tasks_department;
            ALTER TABLE projects.project_tasks DROP CONSTRAINT fk_project_tasks_assigned_to;
            ALTER TABLE projects.projects DROP CONSTRAINT fk_projects_owner;
            """);

        migrationBuilder.DropTable(
            name: "gate_conditions",
            schema: "projects");

        migrationBuilder.DropTable(
            name: "project_stages",
            schema: "projects");

        migrationBuilder.DropTable(
            name: "project_sub_tasks",
            schema: "projects");

        migrationBuilder.DropTable(
            name: "project_tasks",
            schema: "projects");

        migrationBuilder.DropTable(
            name: "projects",
            schema: "projects");

        migrationBuilder.DropTable(
            name: "stage_departments",
            schema: "projects");

        migrationBuilder.DropTable(
            name: "stage_sub_tasks",
            schema: "projects");

        migrationBuilder.DropTable(
            name: "stage_tasks",
            schema: "projects");

        migrationBuilder.DropTable(
            name: "stage_team_members",
            schema: "projects");

        migrationBuilder.DropTable(
            name: "stage_templates",
            schema: "projects");

        migrationBuilder.DropTable(
            name: "task_comments",
            schema: "projects");

        migrationBuilder.DropTable(
            name: "stages",
            schema: "projects");

        migrationBuilder.DropTable(
            name: "stage_teams",
            schema: "projects");
    }
}
