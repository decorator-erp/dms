using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BuildingBlocks.IntegrationTests.Migrations;

/// <inheritdoc />
public partial class AddAuditFixtures : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.EnsureSchema(
            name: "test_module_other");

        migrationBuilder.CreateTable(
            name: "audited_things",
            schema: "test_module",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                Label = table.Column<string>(type: "text", nullable: false),
                PasswordHash = table.Column<string>(type: "text", nullable: false),
                Confidential = table.Column<string>(type: "text", nullable: false),
                IsActive = table.Column<bool>(type: "boolean", nullable: false),
                CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
            },
            constraints: table => table.PrimaryKey("PK_audited_things", x => x.Id));

        migrationBuilder.CreateTable(
            name: "other_schema_audited_things",
            schema: "test_module_other",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                Label = table.Column<string>(type: "text", nullable: false),
                IsActive = table.Column<bool>(type: "boolean", nullable: false),
                CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
            },
            constraints: table => table.PrimaryKey("PK_other_schema_audited_things", x => x.Id));

        migrationBuilder.CreateTable(
            name: "unaudited_things",
            schema: "test_module",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                Label = table.Column<string>(type: "text", nullable: false),
                IsActive = table.Column<bool>(type: "boolean", nullable: false),
                CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
            },
            constraints: table => table.PrimaryKey("PK_unaudited_things", x => x.Id));
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "audited_things",
            schema: "test_module");

        migrationBuilder.DropTable(
            name: "other_schema_audited_things",
            schema: "test_module_other");

        migrationBuilder.DropTable(
            name: "unaudited_things",
            schema: "test_module");
    }
}
