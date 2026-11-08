using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Projects.Infrastructure.Persistence.Migrations.InitialProjects;

/// <inheritdoc />
public partial class AddProjectAccessGrants : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "project_access_grants",
            schema: "projects",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                EmployeeId = table.Column<Guid>(type: "uuid", nullable: false),
                Level = table.Column<int>(type: "integer", nullable: false),
                Areas = table.Column<int>(type: "integer", nullable: false),
                StageIds = table.Column<Guid[]>(type: "uuid[]", nullable: false),
                StartsAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                ExpiresAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                Reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                GrantedById = table.Column<Guid>(type: "uuid", nullable: false),
                GrantedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                RevokedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                RevokedById = table.Column<Guid>(type: "uuid", nullable: true),
                RevokeReason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                IsActive = table.Column<bool>(type: "boolean", nullable: false),
                CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_project_access_grants", x => x.Id);
                table.CheckConstraint("CK_project_access_grants_Areas", "\"Areas\" BETWEEN 1 AND 63");
                table.CheckConstraint("CK_project_access_grants_Level", "\"Level\" BETWEEN 1 AND 3");
                table.CheckConstraint("CK_project_access_grants_Window", "\"ExpiresAtUtc\" IS NULL OR \"ExpiresAtUtc\" > \"StartsAtUtc\"");
                table.ForeignKey(
                    name: "FK_project_access_grants_projects_ProjectId",
                    column: x => x.ProjectId,
                    principalSchema: "projects",
                    principalTable: "projects",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_project_access_grants_EmployeeId_ProjectId",
            schema: "projects",
            table: "project_access_grants",
            columns: new[] { "EmployeeId", "ProjectId" });

        migrationBuilder.CreateIndex(
            name: "IX_project_access_grants_GrantedById",
            schema: "projects",
            table: "project_access_grants",
            column: "GrantedById");

        migrationBuilder.CreateIndex(
            name: "IX_project_access_grants_ProjectId",
            schema: "projects",
            table: "project_access_grants",
            column: "ProjectId");

        migrationBuilder.CreateIndex(
            name: "IX_project_access_grants_RevokedById",
            schema: "projects",
            table: "project_access_grants",
            column: "RevokedById");

        // P1-A: the three bare employee ids get the same cross-schema FK every other hr.employees column in
        // this module has (ON DELETE RESTRICT; EF can't emit it across two DbContext models). The table is new,
        // so there is nothing to orphan-check.
        migrationBuilder.Sql(ForeignKeys);
    }

    private const string ForeignKeys =
        """
        ALTER TABLE projects.project_access_grants
            ADD CONSTRAINT fk_project_access_grants_employee
            FOREIGN KEY ("EmployeeId") REFERENCES hr.employees("Id") ON DELETE RESTRICT;
        ALTER TABLE projects.project_access_grants
            ADD CONSTRAINT fk_project_access_grants_granted_by
            FOREIGN KEY ("GrantedById") REFERENCES hr.employees("Id") ON DELETE RESTRICT;
        ALTER TABLE projects.project_access_grants
            ADD CONSTRAINT fk_project_access_grants_revoked_by
            FOREIGN KEY ("RevokedById") REFERENCES hr.employees("Id") ON DELETE RESTRICT;
        """;

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.DropTable(
            name: "project_access_grants",
            schema: "projects");
}
