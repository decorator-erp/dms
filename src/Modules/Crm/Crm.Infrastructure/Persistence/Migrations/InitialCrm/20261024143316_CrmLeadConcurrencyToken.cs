using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Crm.Infrastructure.Persistence.Migrations.InitialCrm;

/// <inheritdoc />
public partial class CrmLeadConcurrencyToken : Migration
{
    // No-op in SQL: xmin is Postgres' system row-version column, which every table already has.
    // Npgsql's migration SQL generator skips system columns, so this emits no DDL (checked with
    // `dotnet ef migrations script`); EF records it only so the model snapshot knows the token.
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.AddColumn<uint>(
            name: "xmin",
            schema: "crm",
            table: "crm_leads",
            type: "xid",
            rowVersion: true,
            nullable: false,
            defaultValue: 0u);

    // No-op in SQL, like the AddColumn in Up: Npgsql never drops the xmin system column.
    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.DropColumn(
            name: "xmin",
            schema: "crm",
            table: "crm_leads");
}
