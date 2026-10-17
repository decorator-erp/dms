using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BuildingBlocks.IntegrationTests.Migrations;

/// <inheritdoc />
public partial class InitialTestModule : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.EnsureSchema(
            name: "test_module");

        migrationBuilder.CreateTable(
            name: "side_effects",
            schema: "test_module",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                EventId = table.Column<Guid>(type: "uuid", nullable: false),
                HandlerName = table.Column<string>(type: "text", nullable: false)
            },
            constraints: table => table.PrimaryKey("PK_side_effects", x => x.Id));
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.DropTable(
            name: "side_effects",
            schema: "test_module");
}
