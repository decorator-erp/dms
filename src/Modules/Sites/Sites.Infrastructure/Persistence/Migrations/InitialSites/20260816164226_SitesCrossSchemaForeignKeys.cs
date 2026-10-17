using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sites.Infrastructure.Persistence.Migrations.InitialSites;

/// <summary>
/// Sites' seven cross-schema foreign keys. EF cannot emit any of them — the two entities are in
/// different models — so each is raw SQL, with quoted PascalCase columns and
/// <c>ON DELETE RESTRICT</c>.
///
/// <para><b>All seven are legal because sites migrates LAST.</b> Every schema referenced here —
/// projects, hr, crm, documents — already exists by the time this runs. That is the whole
/// difference between this file and CRM's own artifact link, which can never carry a foreign key
/// at all: <c>crm &lt; projects &lt; documents &lt; crm</c> has no valid ordering. Two structurally
/// identical document links, one constrained and one not, for a reason entirely about migration
/// order.</para>
///
/// <para><b>RESTRICT, where the backend uses CASCADE.</b> <c>sites.ProjectId</c> and
/// <c>site_documents.DocumentId</c> are both <c>Cascade</c> in the source. Arch rule 9 forbids
/// CASCADE across a schema boundary — a delete in one module silently destroying another module's
/// rows is precisely what the rule exists to prevent — so both diverge deliberately. The practical
/// effect: a project with surveys must have them removed before it can be deleted, rather than
/// taking them with it.</para>
///
/// <para><b><c>SourceBriefId</c> deliberately gets NO foreign key.</b> It is provenance, not a
/// dependency, and nothing in this system writes it. The backend makes it <c>SET NULL</c> so that a
/// surveyed project stays deletable; rule 9 permits only RESTRICT, and RESTRICT here would make any
/// project whose brief was ever deleted permanently undeletable — the failure surfacing in the
/// projects module against a table its owner has never heard of. Absent is the right answer, and the
/// unique index <c>ux_sites_source_brief</c> still stands for the day something does populate it.</para>
/// </summary>
public partial class SitesCrossSchemaForeignKeys : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // sites -> projects (already in rule 9's allow-list)
        migrationBuilder.Sql("""
            ALTER TABLE sites.sites
                ADD CONSTRAINT fk_sites_project
                FOREIGN KEY ("ProjectId") REFERENCES projects.projects ("Id")
                ON DELETE RESTRICT;
            """);

        // sites -> hr (NEW pair). Three of them: who surveyed, who decided, who attached.
        migrationBuilder.Sql("""
            ALTER TABLE sites.sites
                ADD CONSTRAINT fk_sites_created_by
                FOREIGN KEY ("CreatedById") REFERENCES hr.employees ("Id")
                ON DELETE RESTRICT;
            """);

        migrationBuilder.Sql("""
            ALTER TABLE sites.sites
                ADD CONSTRAINT fk_sites_decided_by
                FOREIGN KEY ("DecidedById") REFERENCES hr.employees ("Id")
                ON DELETE RESTRICT;
            """);

        migrationBuilder.Sql("""
            ALTER TABLE sites.site_documents
                ADD CONSTRAINT fk_site_documents_added_by
                FOREIGN KEY ("AddedById") REFERENCES hr.employees ("Id")
                ON DELETE RESTRICT;
            """);

        // sites -> documents (NEW pair). The link CRM's artifacts cannot have.
        migrationBuilder.Sql("""
            ALTER TABLE sites.site_documents
                ADD CONSTRAINT fk_site_documents_document
                FOREIGN KEY ("DocumentId") REFERENCES documents.project_documents ("Id")
                ON DELETE RESTRICT;
            """);

        // sites -> crm (already in rule 9's allow-list). The FK proves the space EXISTS; it cannot
        // prove the space belongs to this site's project, which is why Crm.Contracts.ICrmSpaceReader
        // exists (S1-01) and why SpaceNotInProject is checked in the handler as well.
        migrationBuilder.Sql("""
            ALTER TABLE sites.site_records
                ADD CONSTRAINT fk_site_records_space
                FOREIGN KEY ("SpaceId") REFERENCES crm.crm_spaces ("Id")
                ON DELETE RESTRICT;
            """);

        migrationBuilder.Sql("""
            ALTER TABLE sites.site_records
                ADD CONSTRAINT fk_site_records_catalog_item
                FOREIGN KEY ("CatalogItemId") REFERENCES crm.crm_catalog_items ("Id")
                ON DELETE RESTRICT;
            """);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            ALTER TABLE sites.site_records DROP CONSTRAINT fk_site_records_catalog_item;
            """);
        migrationBuilder.Sql("""
            ALTER TABLE sites.site_records DROP CONSTRAINT fk_site_records_space;
            """);
        migrationBuilder.Sql("""
            ALTER TABLE sites.site_documents DROP CONSTRAINT fk_site_documents_document;
            """);
        migrationBuilder.Sql("""
            ALTER TABLE sites.site_documents DROP CONSTRAINT fk_site_documents_added_by;
            """);
        migrationBuilder.Sql("""
            ALTER TABLE sites.sites DROP CONSTRAINT fk_sites_decided_by;
            """);
        migrationBuilder.Sql("""
            ALTER TABLE sites.sites DROP CONSTRAINT fk_sites_created_by;
            """);
        migrationBuilder.Sql("""
            ALTER TABLE sites.sites DROP CONSTRAINT fk_sites_project;
            """);
    }
}
