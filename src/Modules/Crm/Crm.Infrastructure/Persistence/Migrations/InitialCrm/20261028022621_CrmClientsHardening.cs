using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Crm.Infrastructure.Persistence.Migrations.InitialCrm;

/// <summary>
/// PR C of the CRM backend completion (clients, contacts, notes). One additive migration:
/// <list type="bullet">
/// <item><description>C-6: <c>xmin</c> becomes the clients' concurrency token. No-op in SQL (Postgres' system row
/// version; see <c>CrmLeadConcurrencyToken</c>).</description></item>
/// <item><description>C-8 (LC-I1): <c>crm.clients.AppUserId</c> becomes unique — one portal login is one client.
/// A guard runs first and refuses the migration while two clients share a login, rather than failing
/// half-way on the index.</description></item>
/// </list>
/// </summary>
public partial class CrmClientsHardening : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            DO $$ BEGIN
              IF EXISTS (
                SELECT 1 FROM crm.clients
                GROUP BY "AppUserId" HAVING COUNT(*) > 1)
              THEN RAISE EXCEPTION 'crm.clients has two clients that share a portal login (AppUserId) — merge or relink them before migrating';
              END IF;
            END $$;
            """);

        migrationBuilder.DropIndex(
            name: "IX_clients_AppUserId",
            schema: "crm",
            table: "clients");

        migrationBuilder.AddColumn<uint>(
            name: "xmin",
            schema: "crm",
            table: "clients",
            type: "xid",
            rowVersion: true,
            nullable: false,
            defaultValue: 0u);

        migrationBuilder.CreateIndex(
            name: "IX_clients_AppUserId",
            schema: "crm",
            table: "clients",
            column: "AppUserId",
            unique: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_clients_AppUserId",
            schema: "crm",
            table: "clients");

        migrationBuilder.DropColumn(
            name: "xmin",
            schema: "crm",
            table: "clients");

        migrationBuilder.CreateIndex(
            name: "IX_clients_AppUserId",
            schema: "crm",
            table: "clients",
            column: "AppUserId");
    }
}
