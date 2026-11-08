using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Crm.Infrastructure.Persistence.Migrations.InitialCrm;

/// <inheritdoc />
public partial class AddClientPortalVisibility : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "crm_portal_settings",
            schema: "crm",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                ShowProgress = table.Column<bool>(type: "boolean", nullable: false),
                ShowTasks = table.Column<bool>(type: "boolean", nullable: false),
                ShowDocuments = table.Column<bool>(type: "boolean", nullable: false),
                ShowGallery = table.Column<bool>(type: "boolean", nullable: false),
                ShowSiteSurveys = table.Column<bool>(type: "boolean", nullable: false),
                ShowUpdates = table.Column<bool>(type: "boolean", nullable: false),
                IsActive = table.Column<bool>(type: "boolean", nullable: false),
                CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
            },
            constraints: table => table.PrimaryKey("PK_crm_portal_settings", x => x.Id));

        migrationBuilder.CreateTable(
            name: "crm_portal_shares",
            schema: "crm",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                ItemKind = table.Column<int>(type: "integer", nullable: false),
                ItemId = table.Column<Guid>(type: "uuid", nullable: false),
                RootDocumentId = table.Column<Guid>(type: "uuid", nullable: true),
                FollowLatest = table.Column<bool>(type: "boolean", nullable: false),
                ClientTitle = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                ClientNote = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                SortOrder = table.Column<int>(type: "integer", nullable: false),
                SharedById = table.Column<Guid>(type: "uuid", nullable: false),
                SharedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                IsActive = table.Column<bool>(type: "boolean", nullable: false),
                CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
            },
            constraints: table => table.PrimaryKey("PK_crm_portal_shares", x => x.Id));

        migrationBuilder.CreateTable(
            name: "crm_portal_updates",
            schema: "crm",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                Title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                Body = table.Column<string>(type: "character varying(10000)", maxLength: 10000, nullable: false),
                PublishedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                AuthorId = table.Column<Guid>(type: "uuid", nullable: false),
                IsActive = table.Column<bool>(type: "boolean", nullable: false),
                CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
            },
            constraints: table => table.PrimaryKey("PK_crm_portal_updates", x => x.Id));

        migrationBuilder.CreateTable(
            name: "crm_portal_update_shares",
            schema: "crm",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                UpdateId = table.Column<Guid>(type: "uuid", nullable: false),
                ShareId = table.Column<Guid>(type: "uuid", nullable: false),
                SortOrder = table.Column<int>(type: "integer", nullable: false),
                IsActive = table.Column<bool>(type: "boolean", nullable: false),
                CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_crm_portal_update_shares", x => x.Id);
                table.ForeignKey(
                    name: "FK_crm_portal_update_shares_crm_portal_shares_ShareId",
                    column: x => x.ShareId,
                    principalSchema: "crm",
                    principalTable: "crm_portal_shares",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_crm_portal_update_shares_crm_portal_updates_UpdateId",
                    column: x => x.UpdateId,
                    principalSchema: "crm",
                    principalTable: "crm_portal_updates",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "ux_crm_portal_settings_project",
            schema: "crm",
            table: "crm_portal_settings",
            column: "ProjectId",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_crm_portal_shares_ProjectId_IsActive_SortOrder",
            schema: "crm",
            table: "crm_portal_shares",
            columns: new[] { "ProjectId", "IsActive", "SortOrder" });

        migrationBuilder.CreateIndex(
            name: "IX_crm_portal_shares_SharedById",
            schema: "crm",
            table: "crm_portal_shares",
            column: "SharedById");

        migrationBuilder.CreateIndex(
            name: "ux_crm_portal_shares_active_item",
            schema: "crm",
            table: "crm_portal_shares",
            columns: new[] { "ProjectId", "ItemKind", "ItemId" },
            unique: true,
            filter: "\"IsActive\"");

        migrationBuilder.CreateIndex(
            name: "IX_crm_portal_update_shares_ShareId",
            schema: "crm",
            table: "crm_portal_update_shares",
            column: "ShareId");

        migrationBuilder.CreateIndex(
            name: "IX_crm_portal_update_shares_UpdateId_ShareId",
            schema: "crm",
            table: "crm_portal_update_shares",
            columns: new[] { "UpdateId", "ShareId" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_crm_portal_updates_AuthorId",
            schema: "crm",
            table: "crm_portal_updates",
            column: "AuthorId");

        migrationBuilder.CreateIndex(
            name: "IX_crm_portal_updates_ProjectId_IsActive",
            schema: "crm",
            table: "crm_portal_updates",
            columns: new[] { "ProjectId", "IsActive" });
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "crm_portal_settings",
            schema: "crm");

        migrationBuilder.DropTable(
            name: "crm_portal_update_shares",
            schema: "crm");

        migrationBuilder.DropTable(
            name: "crm_portal_shares",
            schema: "crm");

        migrationBuilder.DropTable(
            name: "crm_portal_updates",
            schema: "crm");
    }
}
