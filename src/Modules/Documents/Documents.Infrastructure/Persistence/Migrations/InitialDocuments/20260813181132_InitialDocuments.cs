using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Documents.Infrastructure.Persistence.Migrations.InitialDocuments
{
    /// <inheritdoc />
    public partial class InitialDocuments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "documents");

            migrationBuilder.CreateTable(
                name: "document_folders",
                schema: "documents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    ParentFolderId = table.Column<Guid>(type: "uuid", nullable: true),
                    ProjectStageId = table.Column<Guid>(type: "uuid", nullable: true),
                    ProjectTaskId = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDefault = table.Column<bool>(type: "boolean", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_document_folders", x => x.Id);
                    table.ForeignKey(
                        name: "FK_document_folders_document_folders_ParentFolderId",
                        column: x => x.ParentFolderId,
                        principalSchema: "documents",
                        principalTable: "document_folders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "file_templates",
                schema: "documents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Description = table.Column<string>(type: "text", nullable: true),
                    Category = table.Column<int>(type: "integer", nullable: false),
                    FileName = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    StoragePath = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    ContentType = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    FileSizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_file_templates", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "project_documents",
                schema: "documents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    FolderId = table.Column<Guid>(type: "uuid", nullable: true),
                    ProjectStageId = table.Column<Guid>(type: "uuid", nullable: true),
                    ProjectTaskId = table.Column<Guid>(type: "uuid", nullable: true),
                    FileName = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    StoragePath = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    ContentType = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    FileSizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    FileExtension = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Description = table.Column<string>(type: "text", nullable: true),
                    UploadedById = table.Column<Guid>(type: "uuid", nullable: false),
                    UploadedOnUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    RootDocumentId = table.Column<Guid>(type: "uuid", nullable: false),
                    IsLocked = table.Column<bool>(type: "boolean", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_project_documents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_project_documents_document_folders_FolderId",
                        column: x => x.FolderId,
                        principalSchema: "documents",
                        principalTable: "document_folders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_document_folders_ParentFolderId",
                schema: "documents",
                table: "document_folders",
                column: "ParentFolderId");

            migrationBuilder.CreateIndex(
                name: "IX_document_folders_ProjectId",
                schema: "documents",
                table: "document_folders",
                column: "ProjectId");

            migrationBuilder.CreateIndex(
                name: "IX_document_folders_ProjectId_ParentFolderId_Name",
                schema: "documents",
                table: "document_folders",
                columns: new[] { "ProjectId", "ParentFolderId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_document_folders_default_per_stage",
                schema: "documents",
                table: "document_folders",
                column: "ProjectStageId",
                unique: true,
                filter: "\"ProjectStageId\" IS NOT NULL AND \"IsDefault\" = true");

            migrationBuilder.CreateIndex(
                name: "ux_document_folders_default_per_task",
                schema: "documents",
                table: "document_folders",
                column: "ProjectTaskId",
                unique: true,
                filter: "\"ProjectTaskId\" IS NOT NULL AND \"IsDefault\" = true");

            migrationBuilder.CreateIndex(
                name: "IX_file_templates_Category",
                schema: "documents",
                table: "file_templates",
                column: "Category");

            migrationBuilder.CreateIndex(
                name: "IX_file_templates_Name",
                schema: "documents",
                table: "file_templates",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_project_documents_FolderId",
                schema: "documents",
                table: "project_documents",
                column: "FolderId");

            migrationBuilder.CreateIndex(
                name: "IX_project_documents_ProjectId",
                schema: "documents",
                table: "project_documents",
                column: "ProjectId");

            migrationBuilder.CreateIndex(
                name: "IX_project_documents_ProjectStageId",
                schema: "documents",
                table: "project_documents",
                column: "ProjectStageId",
                filter: "\"ProjectStageId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_project_documents_ProjectTaskId",
                schema: "documents",
                table: "project_documents",
                column: "ProjectTaskId",
                filter: "\"ProjectTaskId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_project_documents_root_version_desc",
                schema: "documents",
                table: "project_documents",
                columns: new[] { "RootDocumentId", "Version" },
                unique: true,
                descending: new[] { false, true });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "file_templates",
                schema: "documents");

            migrationBuilder.DropTable(
                name: "project_documents",
                schema: "documents");

            migrationBuilder.DropTable(
                name: "document_folders",
                schema: "documents");
        }
    }
}
