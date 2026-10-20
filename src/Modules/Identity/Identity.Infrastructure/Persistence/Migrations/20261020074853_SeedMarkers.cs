using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Identity.Infrastructure.Persistence.Migrations;

/// <summary>
/// N5: the table behind one-time seed steps (<c>SeedMarker</c>). A new, empty table, so no guard. On
/// the first start after this migration, <c>BaselineClaimSeeder</c> finds no marker and gives the
/// existing <c>employee</c> and <c>client</c> roles their baseline claims once.
/// </summary>
public partial class SeedMarkers : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.CreateTable(
            name: "seed_markers",
            schema: "identity",
            columns: table => new
            {
                Key = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                AppliedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table => table.PrimaryKey("PK_seed_markers", x => x.Key));

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.DropTable(
            name: "seed_markers",
            schema: "identity");
}
