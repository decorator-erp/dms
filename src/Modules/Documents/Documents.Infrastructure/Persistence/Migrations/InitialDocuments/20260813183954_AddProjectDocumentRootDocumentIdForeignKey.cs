using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Documents.Infrastructure.Persistence.Migrations.InitialDocuments
{
    /// <inheritdoc />
    public partial class AddProjectDocumentRootDocumentIdForeignKey : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddForeignKey(
                name: "FK_project_documents_project_documents_RootDocumentId",
                schema: "documents",
                table: "project_documents",
                column: "RootDocumentId",
                principalSchema: "documents",
                principalTable: "project_documents",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_project_documents_project_documents_RootDocumentId",
                schema: "documents",
                table: "project_documents");
        }
    }
}
