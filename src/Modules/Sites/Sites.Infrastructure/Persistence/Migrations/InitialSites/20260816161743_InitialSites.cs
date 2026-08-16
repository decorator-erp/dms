using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sites.Infrastructure.Persistence.Migrations.InitialSites
{
    /// <inheritdoc />
    public partial class InitialSites : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "sites");

            migrationBuilder.CreateTable(
                name: "site_items",
                schema: "sites",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    NameEn = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    NameAr = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    DataType = table.Column<int>(type: "integer", nullable: false),
                    Unit = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    MinValue = table.Column<decimal>(type: "numeric(18,4)", nullable: true),
                    MaxValue = table.Column<decimal>(type: "numeric(18,4)", nullable: true),
                    Tolerance = table.Column<decimal>(type: "numeric(18,3)", nullable: false),
                    MaxLength = table.Column<int>(type: "integer", nullable: true),
                    RegexPattern = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    IsSystem = table.Column<bool>(type: "boolean", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_site_items", x => x.Id);
                    table.CheckConstraint("ck_site_items_type_range", "\"DataType\" BETWEEN 1 AND 5");
                });

            migrationBuilder.CreateTable(
                name: "site_modules",
                schema: "sites",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    NameEn = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    NameAr = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    DescriptionEn = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    DescriptionAr = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    IsSystem = table.Column<bool>(type: "boolean", nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_site_modules", x => x.Id);
                    table.CheckConstraint("ck_site_modules_sort", "\"SortOrder\" >= 0");
                });

            migrationBuilder.CreateTable(
                name: "sites",
                schema: "sites",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    Code = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    Address = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    UnitType = table.Column<int>(type: "integer", nullable: true),
                    UnitCondition = table.Column<int>(type: "integer", nullable: true),
                    AreaSqm = table.Column<decimal>(type: "numeric(8,2)", nullable: true),
                    FloorNo = table.Column<int>(type: "integer", nullable: true),
                    UnitsPerFloor = table.Column<int>(type: "integer", nullable: true),
                    BuildingKind = table.Column<int>(type: "integer", nullable: true),
                    Position = table.Column<int>(type: "integer", nullable: true),
                    ElevatorStatus = table.Column<int>(type: "integer", nullable: true),
                    IsLicensed = table.Column<bool>(type: "boolean", nullable: true),
                    IsRented = table.Column<bool>(type: "boolean", nullable: true),
                    UtilityMetersNote = table.Column<string>(type: "text", nullable: true),
                    SurveyedOn = table.Column<DateOnly>(type: "date", nullable: true),
                    Notes = table.Column<string>(type: "text", nullable: true),
                    SourceBriefId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedById = table.Column<Guid>(type: "uuid", nullable: false),
                    SubmittedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DecidedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DecidedById = table.Column<Guid>(type: "uuid", nullable: true),
                    DecisionNote = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_sites", x => x.Id);
                    table.CheckConstraint("ck_sites_building_numbers", "    (\"AreaSqm\"       IS NULL OR \"AreaSqm\" > 0)\nAND (\"FloorNo\"       IS NULL OR \"FloorNo\"       BETWEEN -5 AND 200)\nAND (\"UnitsPerFloor\" IS NULL OR \"UnitsPerFloor\" BETWEEN 1 AND 100)");
                    table.CheckConstraint("ck_sites_code_format", "\"Code\" ~ '^SITE-[0-9]{5,}$'");
                    table.CheckConstraint("ck_sites_enum_ranges", "    (\"UnitType\"       IS NULL OR \"UnitType\"       BETWEEN 1 AND 4)\nAND (\"UnitCondition\"  IS NULL OR \"UnitCondition\"  BETWEEN 1 AND 3)\nAND (\"BuildingKind\"   IS NULL OR \"BuildingKind\"   BETWEEN 1 AND 2)\nAND (\"Position\"       IS NULL OR \"Position\"       BETWEEN 1 AND 2)\nAND (\"ElevatorStatus\" IS NULL OR \"ElevatorStatus\" BETWEEN 1 AND 3)");
                    table.CheckConstraint("ck_sites_status_range", "\"Status\" BETWEEN 1 AND 4");
                    table.CheckConstraint("ck_sites_status_stamps", "   (\"Status\" = 1      AND \"SubmittedAtUtc\" IS NULL\n                      AND \"DecidedAtUtc\" IS NULL AND \"DecidedById\" IS NULL)\nOR (\"Status\" = 2      AND \"SubmittedAtUtc\" IS NOT NULL\n                      AND \"DecidedAtUtc\" IS NULL AND \"DecidedById\" IS NULL)\nOR (\"Status\" IN (3,4) AND \"SubmittedAtUtc\" IS NOT NULL\n                      AND \"DecidedAtUtc\" IS NOT NULL AND \"DecidedById\" IS NOT NULL)");
                    table.CheckConstraint("ck_sites_submitted_needs_address", "\"Status\" = 1 OR \"Address\" IS NOT NULL");
                });

            migrationBuilder.CreateTable(
                name: "site_item_options",
                schema: "sites",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ItemId = table.Column<Guid>(type: "uuid", nullable: false),
                    Code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    NameEn = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    NameAr = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_site_item_options", x => x.Id);
                    table.CheckConstraint("ck_site_item_options_sort", "\"SortOrder\" >= 0");
                    table.ForeignKey(
                        name: "FK_site_item_options_site_items_ItemId",
                        column: x => x.ItemId,
                        principalSchema: "sites",
                        principalTable: "site_items",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "site_module_items",
                schema: "sites",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ModuleId = table.Column<Guid>(type: "uuid", nullable: false),
                    ItemId = table.Column<Guid>(type: "uuid", nullable: false),
                    IsRequired = table.Column<bool>(type: "boolean", nullable: false),
                    IsVisible = table.Column<bool>(type: "boolean", nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_site_module_items", x => x.Id);
                    table.CheckConstraint("ck_site_module_items_sort", "\"SortOrder\" >= 0");
                    table.ForeignKey(
                        name: "FK_site_module_items_site_items_ItemId",
                        column: x => x.ItemId,
                        principalSchema: "sites",
                        principalTable: "site_items",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_site_module_items_site_modules_ModuleId",
                        column: x => x.ModuleId,
                        principalSchema: "sites",
                        principalTable: "site_modules",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "site_records",
                schema: "sites",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SiteId = table.Column<Guid>(type: "uuid", nullable: false),
                    ModuleId = table.Column<Guid>(type: "uuid", nullable: false),
                    SpaceId = table.Column<Guid>(type: "uuid", nullable: true),
                    CatalogItemId = table.Column<Guid>(type: "uuid", nullable: true),
                    Label = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false),
                    Note = table.Column<string>(type: "text", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_site_records", x => x.Id);
                    table.CheckConstraint("ck_site_records_sort", "\"SortOrder\" >= 0");
                    table.ForeignKey(
                        name: "FK_site_records_site_modules_ModuleId",
                        column: x => x.ModuleId,
                        principalSchema: "sites",
                        principalTable: "site_modules",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_site_records_sites_SiteId",
                        column: x => x.SiteId,
                        principalSchema: "sites",
                        principalTable: "sites",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "site_documents",
                schema: "sites",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    DocumentId = table.Column<Guid>(type: "uuid", nullable: false),
                    SiteId = table.Column<Guid>(type: "uuid", nullable: true),
                    RecordId = table.Column<Guid>(type: "uuid", nullable: true),
                    Kind = table.Column<int>(type: "integer", nullable: false),
                    AddedById = table.Column<Guid>(type: "uuid", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_site_documents", x => x.Id);
                    table.CheckConstraint("ck_site_documents_kind_range", "\"Kind\" BETWEEN 1 AND 3");
                    table.CheckConstraint("ck_site_documents_one_subject", "num_nonnulls(\"SiteId\", \"RecordId\") = 1");
                    table.ForeignKey(
                        name: "FK_site_documents_site_records_RecordId",
                        column: x => x.RecordId,
                        principalSchema: "sites",
                        principalTable: "site_records",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_site_documents_sites_SiteId",
                        column: x => x.SiteId,
                        principalSchema: "sites",
                        principalTable: "sites",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "site_record_values",
                schema: "sites",
                columns: table => new
                {
                    RecordId = table.Column<Guid>(type: "uuid", nullable: false),
                    ItemId = table.Column<Guid>(type: "uuid", nullable: false),
                    ValueNumber = table.Column<decimal>(type: "numeric(18,4)", nullable: true),
                    ValueText = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    ValueBool = table.Column<bool>(type: "boolean", nullable: true),
                    ValueDate = table.Column<DateOnly>(type: "date", nullable: true),
                    OptionId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_site_record_values", x => new { x.RecordId, x.ItemId });
                    table.CheckConstraint("ck_srv_one_value", "num_nonnulls(\"ValueNumber\", \"ValueText\", \"ValueBool\", \"ValueDate\", \"OptionId\") = 1");
                    table.ForeignKey(
                        name: "FK_site_record_values_site_items_ItemId",
                        column: x => x.ItemId,
                        principalSchema: "sites",
                        principalTable: "site_items",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_site_record_values_site_records_RecordId",
                        column: x => x.RecordId,
                        principalSchema: "sites",
                        principalTable: "site_records",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_site_documents_added_by",
                schema: "sites",
                table: "site_documents",
                column: "AddedById");

            migrationBuilder.CreateIndex(
                name: "ix_site_documents_document",
                schema: "sites",
                table: "site_documents",
                column: "DocumentId");

            migrationBuilder.CreateIndex(
                name: "ix_site_documents_record",
                schema: "sites",
                table: "site_documents",
                column: "RecordId",
                filter: "\"RecordId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_site_documents_site",
                schema: "sites",
                table: "site_documents",
                column: "SiteId",
                filter: "\"SiteId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_site_item_options_ItemId",
                schema: "sites",
                table: "site_item_options",
                column: "ItemId");

            migrationBuilder.CreateIndex(
                name: "ix_site_module_items_item",
                schema: "sites",
                table: "site_module_items",
                column: "ItemId");

            migrationBuilder.CreateIndex(
                name: "ix_site_module_items_module_sort",
                schema: "sites",
                table: "site_module_items",
                columns: new[] { "ModuleId", "SortOrder" });

            migrationBuilder.CreateIndex(
                name: "ux_site_module_items_module_item",
                schema: "sites",
                table: "site_module_items",
                columns: new[] { "ModuleId", "ItemId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_site_record_values_facet",
                schema: "sites",
                table: "site_record_values",
                columns: new[] { "ItemId", "OptionId" });

            migrationBuilder.CreateIndex(
                name: "ix_site_records_catalog_item",
                schema: "sites",
                table: "site_records",
                column: "CatalogItemId",
                filter: "\"CatalogItemId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_site_records_module",
                schema: "sites",
                table: "site_records",
                column: "ModuleId");

            migrationBuilder.CreateIndex(
                name: "ix_site_records_site_module",
                schema: "sites",
                table: "site_records",
                columns: new[] { "SiteId", "ModuleId", "SortOrder" });

            migrationBuilder.CreateIndex(
                name: "ix_site_records_space",
                schema: "sites",
                table: "site_records",
                column: "SpaceId",
                filter: "\"SpaceId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_sites_created_by",
                schema: "sites",
                table: "sites",
                column: "CreatedById");

            migrationBuilder.CreateIndex(
                name: "ix_sites_decided_by",
                schema: "sites",
                table: "sites",
                column: "DecidedById",
                filter: "\"DecidedById\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_sites_project_status",
                schema: "sites",
                table: "sites",
                columns: new[] { "ProjectId", "Status" })
                .Annotation("Npgsql:IndexInclude", new[] { "Code", "Address", "SurveyedOn" });

            migrationBuilder.CreateIndex(
                name: "ux_sites_source_brief",
                schema: "sites",
                table: "sites",
                column: "SourceBriefId",
                unique: true,
                filter: "\"SourceBriefId\" IS NOT NULL");

            // ---------------------------------------------------------------------------------------
            // Raw SQL: schema objects EF cannot express.
            //
            // These are INVISIBLE to the probe-migration check -- `dotnet ef migrations add Probe`
            // diffs the EF model against the snapshot, and none of this is in the model, so deleting
            // any of it still yields an empty probe. Sites.Infrastructure.Tests.SitesSchemaObjectTests
            // asserts every one of them against a migrated database instead. Add an object here, add it
            // to that test's list.
            //
            // Every identifier is quoted PascalCase: this solution configures no snake_case naming
            // convention, so an unquoted "code" would fold to a lowercase identifier that does not
            // exist -- silently, and only at runtime.
            // ---------------------------------------------------------------------------------------

            // The site code sequence. Deliberately outside the EF model: the application reads it by
            // name with nextval() and formats SITE-{n:D5}. Nothing else would notice its absence until
            // a site creation failed in production.
            migrationBuilder.Sql("""
                CREATE SEQUENCE sites.site_code_seq AS bigint START WITH 1 INCREMENT BY 1 NO CYCLE;
                """);

            // Case-insensitive uniqueness. Functional indexes have no EF equivalent.
            migrationBuilder.Sql("""
                CREATE UNIQUE INDEX ux_sites_code ON sites.sites (lower("Code"));
                """);

            migrationBuilder.Sql("""
                CREATE UNIQUE INDEX ux_site_modules_code ON sites.site_modules (lower("Code"));
                """);

            migrationBuilder.Sql("""
                CREATE UNIQUE INDEX ux_site_items_code ON sites.site_items (lower("Code"));
                """);

            migrationBuilder.Sql("""
                CREATE UNIQUE INDEX ux_site_item_options_item_code
                    ON sites.site_item_options ("ItemId", lower("Code"));
                """);

            // Partial AND functional. The partiality is load-bearing: soft-deleting a record must free
            // its label for reuse within the same (site, module).
            migrationBuilder.Sql("""
                CREATE UNIQUE INDEX ux_site_records_label
                    ON sites.site_records ("SiteId", "ModuleId", lower("Label"))
                    WHERE "IsActive";
                """);

            // Blank-string guards. NOT NULL cannot express "not whitespace", and the domain's own
            // trim-then-reject is only as good as the one code path that runs it.
            migrationBuilder.Sql("""
                ALTER TABLE sites.sites
                    ADD CONSTRAINT ck_sites_address_blank
                    CHECK ("Address" IS NULL OR btrim("Address") <> '');
                """);

            migrationBuilder.Sql("""
                ALTER TABLE sites.site_modules
                    ADD CONSTRAINT ck_site_modules_code_blank CHECK (btrim("Code") <> '');
                """);

            migrationBuilder.Sql("""
                ALTER TABLE sites.site_items
                    ADD CONSTRAINT ck_site_items_code_blank CHECK (btrim("Code") <> '');
                """);

            migrationBuilder.Sql("""
                ALTER TABLE sites.site_item_options
                    ADD CONSTRAINT ck_site_item_options_code_blank CHECK (btrim("Code") <> '');
                """);

            migrationBuilder.Sql("""
                ALTER TABLE sites.site_records
                    ADD CONSTRAINT ck_site_records_label_blank CHECK (btrim("Label") <> '');
                """);

            // The five-clause limit rule, the database twin of SiteItem.ValidateLimits. Each limit
            // family is legal on exactly one data type -- a bound on a Select would be silently ignored
            // by the validator, leaving its author believing a rule was in force.
            migrationBuilder.Sql("""
                ALTER TABLE sites.site_items
                    ADD CONSTRAINT ck_site_items_range CHECK (
                            ("MinValue" IS NULL OR "MaxValue" IS NULL OR "MinValue" <= "MaxValue")
                        AND ("MaxLength" IS NULL OR "MaxLength" BETWEEN 1 AND 1024)
                        AND ("Tolerance" >= 0)
                        AND ("DataType" = 1 OR ("MinValue" IS NULL AND "MaxValue" IS NULL AND "Tolerance" = 0))
                        AND ("DataType" = 2 OR ("MaxLength" IS NULL AND "RegexPattern" IS NULL))
                    );
                """);

            // The composite unique key site_record_values' option FK points at. It exists ONLY so that
            // FK has a target: Postgres refuses a composite foreign key against anything less.
            migrationBuilder.Sql("""
                ALTER TABLE sites.site_item_options
                    ADD CONSTRAINT uq_site_item_options_item_id UNIQUE ("ItemId", "Id");
                """);

            // The constraint that actually enforces "the chosen option belongs to THIS item". EF cannot
            // model a composite FK to a non-primary unique key, so both halves are hand-written. Without
            // it, a value could name any option in the table and only the application check would object.
            migrationBuilder.Sql("""
                ALTER TABLE sites.site_record_values
                    ADD CONSTRAINT fk_site_record_values_option_of_item
                    FOREIGN KEY ("ItemId", "OptionId")
                    REFERENCES sites.site_item_options ("ItemId", "Id")
                    ON DELETE RESTRICT;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Raw-SQL objects come down first: the composite FK and the unique key it targets must go
            // before their tables, and dropping a constraint that a later DropTable would remove anyway
            // is harmless while the reverse order is not.
            migrationBuilder.Sql("""
                ALTER TABLE sites.site_record_values DROP CONSTRAINT fk_site_record_values_option_of_item;
                """);
            migrationBuilder.Sql("""
                ALTER TABLE sites.site_item_options DROP CONSTRAINT uq_site_item_options_item_id;
                """);
            migrationBuilder.Sql("""
                ALTER TABLE sites.site_items DROP CONSTRAINT ck_site_items_range;
                """);
            migrationBuilder.Sql("""
                ALTER TABLE sites.site_records DROP CONSTRAINT ck_site_records_label_blank;
                """);
            migrationBuilder.Sql("""
                ALTER TABLE sites.site_item_options DROP CONSTRAINT ck_site_item_options_code_blank;
                """);
            migrationBuilder.Sql("""
                ALTER TABLE sites.site_items DROP CONSTRAINT ck_site_items_code_blank;
                """);
            migrationBuilder.Sql("""
                ALTER TABLE sites.site_modules DROP CONSTRAINT ck_site_modules_code_blank;
                """);
            migrationBuilder.Sql("""
                ALTER TABLE sites.sites DROP CONSTRAINT ck_sites_address_blank;
                """);
            migrationBuilder.Sql("DROP INDEX sites.ux_site_records_label;");
            migrationBuilder.Sql("DROP INDEX sites.ux_site_item_options_item_code;");
            migrationBuilder.Sql("DROP INDEX sites.ux_site_items_code;");
            migrationBuilder.Sql("DROP INDEX sites.ux_site_modules_code;");
            migrationBuilder.Sql("DROP INDEX sites.ux_sites_code;");
            migrationBuilder.Sql("DROP SEQUENCE sites.site_code_seq;");

            migrationBuilder.DropTable(
                name: "site_documents",
                schema: "sites");

            migrationBuilder.DropTable(
                name: "site_item_options",
                schema: "sites");

            migrationBuilder.DropTable(
                name: "site_module_items",
                schema: "sites");

            migrationBuilder.DropTable(
                name: "site_record_values",
                schema: "sites");

            migrationBuilder.DropTable(
                name: "site_items",
                schema: "sites");

            migrationBuilder.DropTable(
                name: "site_records",
                schema: "sites");

            migrationBuilder.DropTable(
                name: "site_modules",
                schema: "sites");

            migrationBuilder.DropTable(
                name: "sites",
                schema: "sites");
        }
    }
}
