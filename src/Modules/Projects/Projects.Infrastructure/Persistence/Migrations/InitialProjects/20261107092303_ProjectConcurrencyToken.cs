using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Projects.Infrastructure.Persistence.Migrations.InitialProjects;

/// <inheritdoc />
public partial class ProjectConcurrencyToken : Migration
{
    // P9. No-op in SQL: xmin is Postgres' system row-version column, which every table already has.
    // Npgsql's migration SQL generator skips system columns, so this emits no DDL; EF records it only so
    // the model snapshot knows the token (the CrmLeadConcurrencyToken template).
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.AddColumn<uint>(
            name: "xmin",
            schema: "projects",
            table: "projects",
            type: "xid",
            rowVersion: true,
            nullable: false,
            defaultValue: 0u);

    // No-op in SQL, like the AddColumn in Up: Npgsql never drops the xmin system column.
    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.DropColumn(
            name: "xmin",
            schema: "projects",
            table: "projects");
}
