using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Crm.Infrastructure.Persistence.Migrations.InitialCrm;

/// <inheritdoc />
public partial class CrmCompleteCrm : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(CrmCompleteCrmSql.Guard);

        migrationBuilder.AddColumn<string>(
            name: "Selections",
            schema: "crm",
            table: "crm_brief_spaces",
            type: "jsonb",
            nullable: true);

        migrationBuilder.CreateTable(
            name: "crm_space_selections",
            schema: "crm",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                SpaceId = table.Column<Guid>(type: "uuid", nullable: false),
                CatalogItemId = table.Column<Guid>(type: "uuid", nullable: false),
                Quantity = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                Notes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                SortOrder = table.Column<int>(type: "integer", nullable: false),
                xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                IsActive = table.Column<bool>(type: "boolean", nullable: false),
                CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_crm_space_selections", x => x.Id);
                table.ForeignKey(
                    name: "FK_crm_space_selections_crm_catalog_items_CatalogItemId",
                    column: x => x.CatalogItemId,
                    principalSchema: "crm",
                    principalTable: "crm_catalog_items",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_crm_space_selections_crm_spaces_SpaceId",
                    column: x => x.SpaceId,
                    principalSchema: "crm",
                    principalTable: "crm_spaces",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "crm_space_selection_options",
            schema: "crm",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                SelectionId = table.Column<Guid>(type: "uuid", nullable: false),
                AttributeId = table.Column<Guid>(type: "uuid", nullable: false),
                OptionId = table.Column<Guid>(type: "uuid", nullable: false),
                IsActive = table.Column<bool>(type: "boolean", nullable: false),
                CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_crm_space_selection_options", x => x.Id);
                table.ForeignKey(
                    name: "FK_crm_space_selection_options_crm_catalog_attribute_options_O~",
                    column: x => x.OptionId,
                    principalSchema: "crm",
                    principalTable: "crm_catalog_attribute_options",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_crm_space_selection_options_crm_catalog_attributes_Attribut~",
                    column: x => x.AttributeId,
                    principalSchema: "crm",
                    principalTable: "crm_catalog_attributes",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_crm_space_selection_options_crm_space_selections_SelectionId",
                    column: x => x.SelectionId,
                    principalSchema: "crm",
                    principalTable: "crm_space_selections",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_crm_space_selection_options_AttributeId",
            schema: "crm",
            table: "crm_space_selection_options",
            column: "AttributeId");

        migrationBuilder.CreateIndex(
            name: "IX_crm_space_selection_options_OptionId",
            schema: "crm",
            table: "crm_space_selection_options",
            column: "OptionId");

        migrationBuilder.CreateIndex(
            name: "IX_crm_space_selection_options_SelectionId_AttributeId",
            schema: "crm",
            table: "crm_space_selection_options",
            columns: new[] { "SelectionId", "AttributeId" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_crm_space_selections_CatalogItemId",
            schema: "crm",
            table: "crm_space_selections",
            column: "CatalogItemId");

        migrationBuilder.CreateIndex(
            name: "IX_crm_space_selections_SpaceId_SortOrder",
            schema: "crm",
            table: "crm_space_selections",
            columns: new[] { "SpaceId", "SortOrder" });
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "crm_space_selection_options",
            schema: "crm");

        migrationBuilder.DropTable(
            name: "crm_space_selections",
            schema: "crm");

        migrationBuilder.DropColumn(
            name: "Selections",
            schema: "crm",
            table: "crm_brief_spaces");
    }
}
