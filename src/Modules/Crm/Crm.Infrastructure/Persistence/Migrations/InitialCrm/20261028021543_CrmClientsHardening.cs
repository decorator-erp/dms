using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Crm.Infrastructure.Persistence.Migrations.InitialCrm;

/// <summary>
/// PR C of the CRM backend completion (clients, contacts, notes). One additive migration:
/// <list type="bullet">
/// <item><description>C-6: <c>xmin</c> becomes the clients' concurrency token. No-op in SQL (Postgres' system row
/// version; see <c>CrmLeadConcurrencyToken</c>).</description></item>
/// </list>
/// </summary>
public partial class CrmClientsHardening : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.AddColumn<uint>(
            name: "xmin",
            schema: "crm",
            table: "clients",
            type: "xid",
            rowVersion: true,
            nullable: false,
            defaultValue: 0u);

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.DropColumn(
            name: "xmin",
            schema: "crm",
            table: "clients");
}
