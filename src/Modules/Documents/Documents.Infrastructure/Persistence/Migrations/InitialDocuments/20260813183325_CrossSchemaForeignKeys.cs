using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Documents.Infrastructure.Persistence.Migrations.InitialDocuments
{
    /// <inheritdoc />
    /// <remarks>
    /// G7-04: the seven cross-schema foreign keys from documents.* into projects.*/hr.employees.
    /// documents → projects and documents → hr are both already in rule 9's allow-list. Hand-written via
    /// raw SQL - EF cannot emit a cross-schema FK, the two entities live in different models. Each
    /// referencing column already has its own index (project_documents.UploadedById's added in this
    /// same migration, above; every other referencing column was already indexed at G7-03).
    /// </remarks>
    public partial class CrossSchemaForeignKeys : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_project_documents_UploadedById",
                schema: "documents",
                table: "project_documents",
                column: "UploadedById");

            migrationBuilder.Sql(
                """
                ALTER TABLE documents.project_documents
                    ADD CONSTRAINT fk_project_documents_project
                    FOREIGN KEY ("ProjectId") REFERENCES projects.projects("Id")
                    ON DELETE RESTRICT;

                ALTER TABLE documents.project_documents
                    ADD CONSTRAINT fk_project_documents_project_stage
                    FOREIGN KEY ("ProjectStageId") REFERENCES projects.project_stages("Id")
                    ON DELETE RESTRICT;

                ALTER TABLE documents.project_documents
                    ADD CONSTRAINT fk_project_documents_project_task
                    FOREIGN KEY ("ProjectTaskId") REFERENCES projects.project_tasks("Id")
                    ON DELETE RESTRICT;

                ALTER TABLE documents.project_documents
                    ADD CONSTRAINT fk_project_documents_uploaded_by
                    FOREIGN KEY ("UploadedById") REFERENCES hr.employees("Id")
                    ON DELETE RESTRICT;

                ALTER TABLE documents.document_folders
                    ADD CONSTRAINT fk_document_folders_project
                    FOREIGN KEY ("ProjectId") REFERENCES projects.projects("Id")
                    ON DELETE RESTRICT;

                ALTER TABLE documents.document_folders
                    ADD CONSTRAINT fk_document_folders_project_stage
                    FOREIGN KEY ("ProjectStageId") REFERENCES projects.project_stages("Id")
                    ON DELETE RESTRICT;

                ALTER TABLE documents.document_folders
                    ADD CONSTRAINT fk_document_folders_project_task
                    FOREIGN KEY ("ProjectTaskId") REFERENCES projects.project_tasks("Id")
                    ON DELETE RESTRICT;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                ALTER TABLE documents.document_folders DROP CONSTRAINT fk_document_folders_project_task;
                ALTER TABLE documents.document_folders DROP CONSTRAINT fk_document_folders_project_stage;
                ALTER TABLE documents.document_folders DROP CONSTRAINT fk_document_folders_project;
                ALTER TABLE documents.project_documents DROP CONSTRAINT fk_project_documents_uploaded_by;
                ALTER TABLE documents.project_documents DROP CONSTRAINT fk_project_documents_project_task;
                ALTER TABLE documents.project_documents DROP CONSTRAINT fk_project_documents_project_stage;
                ALTER TABLE documents.project_documents DROP CONSTRAINT fk_project_documents_project;
                """);

            migrationBuilder.DropIndex(
                name: "IX_project_documents_UploadedById",
                schema: "documents",
                table: "project_documents");
        }
    }
}
