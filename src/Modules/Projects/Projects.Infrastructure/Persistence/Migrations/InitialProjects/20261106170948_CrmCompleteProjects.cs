using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Projects.Infrastructure.Persistence.Migrations.InitialProjects;

/// <inheritdoc />
public partial class CrmCompleteProjects : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(CrmCompleteProjectsSql.Guard);

        migrationBuilder.CreateTable(
            name: "project_members",
            schema: "projects",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                EmployeeId = table.Column<Guid>(type: "uuid", nullable: false),
                Access = table.Column<int>(type: "integer", nullable: false),
                AddedById = table.Column<Guid>(type: "uuid", nullable: false),
                AddedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                IsActive = table.Column<bool>(type: "boolean", nullable: false),
                CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_project_members", x => x.Id);
                table.ForeignKey(
                    name: "FK_project_members_projects_ProjectId",
                    column: x => x.ProjectId,
                    principalSchema: "projects",
                    principalTable: "projects",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_project_members_EmployeeId",
            schema: "projects",
            table: "project_members",
            column: "EmployeeId");

        migrationBuilder.CreateIndex(
            name: "IX_project_members_ProjectId_EmployeeId",
            schema: "projects",
            table: "project_members",
            columns: new[] { "ProjectId", "EmployeeId" },
            unique: true);

        migrationBuilder.Sql(CrmCompleteProjectsSql.ForeignKeys);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(CrmCompleteProjectsSql.DropForeignKeys);

        migrationBuilder.DropTable(
            name: "project_members",
            schema: "projects");
    }
}
