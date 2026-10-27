using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Projects.Infrastructure.Persistence.Migrations.InitialProjects;

/// <inheritdoc />
public partial class ScopeOfWork : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<decimal>(
            name: "DefaultFee",
            schema: "projects",
            table: "stages",
            type: "numeric(18,2)",
            precision: 18,
            scale: 2,
            nullable: true);

        migrationBuilder.CreateTable(
            name: "scopes_of_work",
            schema: "projects",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                Status = table.Column<int>(type: "integer", nullable: false),
                DurationDays = table.Column<int>(type: "integer", nullable: true),
                PlannedStartDate = table.Column<DateOnly>(type: "date", nullable: true),
                AdjustmentAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                AdjustmentNote = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                Notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                ConfirmedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                ConfirmedById = table.Column<Guid>(type: "uuid", nullable: true),
                xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                IsActive = table.Column<bool>(type: "boolean", nullable: false),
                CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_scopes_of_work", x => x.Id);
                table.ForeignKey(
                    name: "FK_scopes_of_work_projects_ProjectId",
                    column: x => x.ProjectId,
                    principalSchema: "projects",
                    principalTable: "projects",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "scope_of_work_items",
            schema: "projects",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                ScopeOfWorkId = table.Column<Guid>(type: "uuid", nullable: false),
                SourceStageId = table.Column<Guid>(type: "uuid", nullable: false),
                Name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                Description = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                Order = table.Column<int>(type: "integer", nullable: false),
                Fee = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                CreatedStageId = table.Column<Guid>(type: "uuid", nullable: true),
                IsActive = table.Column<bool>(type: "boolean", nullable: false),
                CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_scope_of_work_items", x => x.Id);
                table.ForeignKey(
                    name: "FK_scope_of_work_items_scopes_of_work_ScopeOfWorkId",
                    column: x => x.ScopeOfWorkId,
                    principalSchema: "projects",
                    principalTable: "scopes_of_work",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_scope_of_work_items_ScopeOfWorkId",
            schema: "projects",
            table: "scope_of_work_items",
            column: "ScopeOfWorkId");

        migrationBuilder.CreateIndex(
            name: "IX_scope_of_work_items_ScopeOfWorkId_Order",
            schema: "projects",
            table: "scope_of_work_items",
            columns: new[] { "ScopeOfWorkId", "Order" });

        migrationBuilder.CreateIndex(
            name: "IX_scope_of_work_items_ScopeOfWorkId_SourceStageId",
            schema: "projects",
            table: "scope_of_work_items",
            columns: new[] { "ScopeOfWorkId", "SourceStageId" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_scopes_of_work_ProjectId",
            schema: "projects",
            table: "scopes_of_work",
            column: "ProjectId",
            unique: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "scope_of_work_items",
            schema: "projects");

        migrationBuilder.DropTable(
            name: "scopes_of_work",
            schema: "projects");

        migrationBuilder.DropColumn(
            name: "DefaultFee",
            schema: "projects",
            table: "stages");
    }
}
