using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Projects.Infrastructure.Persistence.Migrations.InitialProjects;

/// <inheritdoc />
/// <remarks>
/// G9-13: closes the Phase 2 deferral — <c>projects.projects.ClientId</c> gets its FK into
/// <c>crm.clients(Id)</c> now that CRM exists. Legal per rule 9's allow-list (<c>projects → crm</c>
/// was already present, unlike the reverse — see Crm.Infrastructure's own
/// <c>CrossSchemaForeignKeys</c> migration remarks for why <c>crm → projects</c> can never exist).
/// Hand-written via raw SQL, same reason as every other cross-schema FK in this repo: EF cannot
/// emit a FK between two different <c>DbContext</c> models. <c>ON DELETE RESTRICT</c>, one index
/// on the referencing column, matching every other cross-schema FK migration's shape.
///
/// <b>No production backfill</b> — nothing is deployed yet and every test run starts from an empty
/// database (same reasoning as E1-01's own migration).
/// </remarks>
public partial class AddClientForeignKey : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateIndex(
            name: "IX_projects_ClientId",
            schema: "projects",
            table: "projects",
            column: "ClientId");

        migrationBuilder.Sql(
            """
            ALTER TABLE projects.projects
                ADD CONSTRAINT fk_projects_client
                FOREIGN KEY ("ClientId") REFERENCES crm.clients("Id")
                ON DELETE RESTRICT;
            """);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            ALTER TABLE projects.projects DROP CONSTRAINT fk_projects_client;
            """);

        migrationBuilder.DropIndex(
            name: "IX_projects_ClientId",
            schema: "projects",
            table: "projects");
    }
}
