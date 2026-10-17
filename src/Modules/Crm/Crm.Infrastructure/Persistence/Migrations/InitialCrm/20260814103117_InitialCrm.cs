using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Crm.Infrastructure.Persistence.Migrations.InitialCrm;

/// <inheritdoc />
public partial class InitialCrm : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.EnsureSchema(
            name: "crm");

        migrationBuilder.CreateTable(
            name: "clients",
            schema: "crm",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                AppUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false),
                Address = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                Notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                JobTitle = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                MaritalStatus = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                RepresentativeName = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                IsActive = table.Column<bool>(type: "boolean", nullable: false),
                CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                children = table.Column<string>(type: "jsonb", nullable: true)
            },
            constraints: table => table.PrimaryKey("PK_clients", x => x.Id));

        migrationBuilder.CreateTable(
            name: "crm_catalog_categories",
            schema: "crm",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                ParentId = table.Column<Guid>(type: "uuid", nullable: true),
                Code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                NameEn = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                NameAr = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                DescriptionEn = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                DescriptionAr = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                ImageUri = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                SelectionMode = table.Column<int>(type: "integer", nullable: false),
                IsRequired = table.Column<bool>(type: "boolean", nullable: false),
                SortOrder = table.Column<int>(type: "integer", nullable: false),
                Path = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                Depth = table.Column<int>(type: "integer", nullable: false),
                IsActive = table.Column<bool>(type: "boolean", nullable: false),
                CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_crm_catalog_categories", x => x.Id);
                table.ForeignKey(
                    name: "FK_crm_catalog_categories_crm_catalog_categories_ParentId",
                    column: x => x.ParentId,
                    principalSchema: "crm",
                    principalTable: "crm_catalog_categories",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "crm_design_briefs",
            schema: "crm",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                ProjectStageId = table.Column<Guid>(type: "uuid", nullable: true),
                SiteId = table.Column<Guid>(type: "uuid", nullable: true),
                CreatedById = table.Column<Guid>(type: "uuid", nullable: false),
                LastMeetingId = table.Column<Guid>(type: "uuid", nullable: true),
                Status = table.Column<int>(type: "integer", nullable: false),
                UnitType = table.Column<int>(type: "integer", nullable: false),
                UnitCondition = table.Column<int>(type: "integer", nullable: false),
                Address = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                FloorNo = table.Column<int>(type: "integer", nullable: true),
                UnitsPerFloor = table.Column<int>(type: "integer", nullable: true),
                AreaSqm = table.Column<decimal>(type: "numeric", nullable: true),
                BuildingKind = table.Column<int>(type: "integer", nullable: true),
                IsLicensed = table.Column<bool>(type: "boolean", nullable: true),
                Position = table.Column<int>(type: "integer", nullable: true),
                ElevatorStatus = table.Column<int>(type: "integer", nullable: true),
                ReceivesFrequentVisitors = table.Column<bool>(type: "boolean", nullable: true),
                VisitorsNote = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                UsedDesignerBefore = table.Column<bool>(type: "boolean", nullable: true),
                DesignerExperience = table.Column<int>(type: "integer", nullable: true),
                DesignerExperienceReason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                HiringReasons = table.Column<int>(type: "integer", nullable: false),
                PreferredStyle = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                PreferredColors = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                FurnitureStatus = table.Column<int>(type: "integer", nullable: true),
                ExpectedBudget = table.Column<decimal>(type: "numeric", nullable: true),
                TimelinePressure = table.Column<int>(type: "integer", nullable: true),
                ArchitecturalRequests = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                OtherNeeds = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                Sector = table.Column<int>(type: "integer", nullable: true),
                IsRented = table.Column<bool>(type: "boolean", nullable: true),
                FinishingPurpose = table.Column<int>(type: "integer", nullable: true),
                IsActive = table.Column<bool>(type: "boolean", nullable: false),
                CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
            },
            constraints: table => table.PrimaryKey("PK_crm_design_briefs", x => x.Id));

        migrationBuilder.CreateTable(
            name: "crm_requirement_periods",
            schema: "crm",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                ProjectStageId = table.Column<Guid>(type: "uuid", nullable: true),
                Status = table.Column<int>(type: "integer", nullable: false),
                ClosesAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                ClosedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                CloseNotifiedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                OpenedById = table.Column<Guid>(type: "uuid", nullable: false),
                IsActive = table.Column<bool>(type: "boolean", nullable: false),
                CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
            },
            constraints: table => table.PrimaryKey("PK_crm_requirement_periods", x => x.Id));

        migrationBuilder.CreateTable(
            name: "crm_space_types",
            schema: "crm",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                Code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                NameEn = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                NameAr = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                SortOrder = table.Column<int>(type: "integer", nullable: false),
                IsActive = table.Column<bool>(type: "boolean", nullable: false),
                CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
            },
            constraints: table => table.PrimaryKey("PK_crm_space_types", x => x.Id));

        migrationBuilder.CreateTable(
            name: "crm_client_notes",
            schema: "crm",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                ClientId = table.Column<Guid>(type: "uuid", nullable: false),
                ProjectId = table.Column<Guid>(type: "uuid", nullable: true),
                Content = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                WrittenById = table.Column<Guid>(type: "uuid", nullable: false),
                IsActive = table.Column<bool>(type: "boolean", nullable: false),
                CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_crm_client_notes", x => x.Id);
                table.ForeignKey(
                    name: "FK_crm_client_notes_clients_ClientId",
                    column: x => x.ClientId,
                    principalSchema: "crm",
                    principalTable: "clients",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "crm_contacts",
            schema: "crm",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                ClientId = table.Column<Guid>(type: "uuid", nullable: false),
                FirstName = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                LastName = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                Email = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                Phone = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                Mobile = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                Role = table.Column<int>(type: "integer", nullable: false),
                IsPrimary = table.Column<bool>(type: "boolean", nullable: false),
                Notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                IsActive = table.Column<bool>(type: "boolean", nullable: false),
                CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_crm_contacts", x => x.Id);
                table.ForeignKey(
                    name: "FK_crm_contacts_clients_ClientId",
                    column: x => x.ClientId,
                    principalSchema: "crm",
                    principalTable: "clients",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "crm_leads",
            schema: "crm",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                FirstName = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                LastName = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                Email = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                Phone = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                Source = table.Column<int>(type: "integer", nullable: false),
                Status = table.Column<int>(type: "integer", nullable: false),
                OwnerId = table.Column<Guid>(type: "uuid", nullable: false),
                Notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                ConvertedClientId = table.Column<Guid>(type: "uuid", nullable: true),
                ConvertedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                IsActive = table.Column<bool>(type: "boolean", nullable: false),
                CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_crm_leads", x => x.Id);
                table.ForeignKey(
                    name: "FK_crm_leads_clients_ConvertedClientId",
                    column: x => x.ConvertedClientId,
                    principalSchema: "crm",
                    principalTable: "clients",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "crm_catalog_attributes",
            schema: "crm",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                CategoryId = table.Column<Guid>(type: "uuid", nullable: false),
                Code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                NameEn = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                NameAr = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                DataType = table.Column<int>(type: "integer", nullable: false),
                Unit = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                IsRequired = table.Column<bool>(type: "boolean", nullable: false),
                SortOrder = table.Column<int>(type: "integer", nullable: false),
                IsActive = table.Column<bool>(type: "boolean", nullable: false),
                CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_crm_catalog_attributes", x => x.Id);
                table.ForeignKey(
                    name: "FK_crm_catalog_attributes_crm_catalog_categories_CategoryId",
                    column: x => x.CategoryId,
                    principalSchema: "crm",
                    principalTable: "crm_catalog_categories",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "crm_catalog_items",
            schema: "crm",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                CategoryId = table.Column<Guid>(type: "uuid", nullable: false),
                Code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                NameEn = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                NameAr = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                DescriptionEn = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                DescriptionAr = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                ImageUri = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                IsPurchasable = table.Column<bool>(type: "boolean", nullable: false),
                SortOrder = table.Column<int>(type: "integer", nullable: false),
                IsActive = table.Column<bool>(type: "boolean", nullable: false),
                CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_crm_catalog_items", x => x.Id);
                table.ForeignKey(
                    name: "FK_crm_catalog_items_crm_catalog_categories_CategoryId",
                    column: x => x.CategoryId,
                    principalSchema: "crm",
                    principalTable: "crm_catalog_categories",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "crm_brief_head_snapshots",
            schema: "crm",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                BriefId = table.Column<Guid>(type: "uuid", nullable: false),
                ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                Round = table.Column<int>(type: "integer", nullable: false),
                UnitType = table.Column<int>(type: "integer", nullable: false),
                UnitCondition = table.Column<int>(type: "integer", nullable: false),
                Address = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                FloorNo = table.Column<int>(type: "integer", nullable: true),
                UnitsPerFloor = table.Column<int>(type: "integer", nullable: true),
                AreaSqm = table.Column<decimal>(type: "numeric", nullable: true),
                BuildingKind = table.Column<int>(type: "integer", nullable: true),
                IsLicensed = table.Column<bool>(type: "boolean", nullable: true),
                Position = table.Column<int>(type: "integer", nullable: true),
                ElevatorStatus = table.Column<int>(type: "integer", nullable: true),
                ReceivesFrequentVisitors = table.Column<bool>(type: "boolean", nullable: true),
                VisitorsNote = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                UsedDesignerBefore = table.Column<bool>(type: "boolean", nullable: true),
                DesignerExperience = table.Column<int>(type: "integer", nullable: true),
                DesignerExperienceReason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                HiringReasons = table.Column<int>(type: "integer", nullable: false),
                PreferredStyle = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                PreferredColors = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                FurnitureStatus = table.Column<int>(type: "integer", nullable: true),
                ExpectedBudget = table.Column<decimal>(type: "numeric", nullable: true),
                TimelinePressure = table.Column<int>(type: "integer", nullable: true),
                ArchitecturalRequests = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                OtherNeeds = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                Sector = table.Column<int>(type: "integer", nullable: true),
                IsRented = table.Column<bool>(type: "boolean", nullable: true),
                FinishingPurpose = table.Column<int>(type: "integer", nullable: true),
                LastMeetingId = table.Column<Guid>(type: "uuid", nullable: true),
                IsActive = table.Column<bool>(type: "boolean", nullable: false),
                CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_crm_brief_head_snapshots", x => x.Id);
                table.ForeignKey(
                    name: "FK_crm_brief_head_snapshots_crm_design_briefs_BriefId",
                    column: x => x.BriefId,
                    principalSchema: "crm",
                    principalTable: "crm_design_briefs",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "crm_catalog_category_space_types",
            schema: "crm",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                CategoryId = table.Column<Guid>(type: "uuid", nullable: false),
                SpaceTypeId = table.Column<Guid>(type: "uuid", nullable: false),
                IsActive = table.Column<bool>(type: "boolean", nullable: false),
                CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_crm_catalog_category_space_types", x => x.Id);
                table.ForeignKey(
                    name: "FK_crm_catalog_category_space_types_crm_catalog_categories_Cat~",
                    column: x => x.CategoryId,
                    principalSchema: "crm",
                    principalTable: "crm_catalog_categories",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "FK_crm_catalog_category_space_types_crm_space_types_SpaceTypeId",
                    column: x => x.SpaceTypeId,
                    principalSchema: "crm",
                    principalTable: "crm_space_types",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "crm_spaces",
            schema: "crm",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                SpaceTypeId = table.Column<Guid>(type: "uuid", nullable: false),
                Name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                Description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                SortOrder = table.Column<int>(type: "integer", nullable: false),
                IsActive = table.Column<bool>(type: "boolean", nullable: false),
                CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_crm_spaces", x => x.Id);
                table.ForeignKey(
                    name: "FK_crm_spaces_crm_space_types_SpaceTypeId",
                    column: x => x.SpaceTypeId,
                    principalSchema: "crm",
                    principalTable: "crm_space_types",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "crm_meetings",
            schema: "crm",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                PeriodId = table.Column<Guid>(type: "uuid", nullable: true),
                Title = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                Type = table.Column<int>(type: "integer", nullable: false),
                Status = table.Column<int>(type: "integer", nullable: false),
                ScheduledAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                DurationMinutes = table.Column<int>(type: "integer", nullable: true),
                Location = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                RequestedByEmployeeId = table.Column<Guid>(type: "uuid", nullable: true),
                RequestedByContactId = table.Column<Guid>(type: "uuid", nullable: true),
                ReminderSentAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                CanceledAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                CancelReason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                IsActive = table.Column<bool>(type: "boolean", nullable: false),
                CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_crm_meetings", x => x.Id);
                table.ForeignKey(
                    name: "FK_crm_meetings_crm_contacts_RequestedByContactId",
                    column: x => x.RequestedByContactId,
                    principalSchema: "crm",
                    principalTable: "crm_contacts",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_crm_meetings_crm_requirement_periods_PeriodId",
                    column: x => x.PeriodId,
                    principalSchema: "crm",
                    principalTable: "crm_requirement_periods",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "crm_requirement_items",
            schema: "crm",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                PeriodId = table.Column<Guid>(type: "uuid", nullable: false),
                Category = table.Column<int>(type: "integer", nullable: false),
                SpaceTypeId = table.Column<Guid>(type: "uuid", nullable: true),
                Room = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                Title = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                Description = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                Priority = table.Column<int>(type: "integer", nullable: false),
                SourceType = table.Column<int>(type: "integer", nullable: true),
                MeetingId = table.Column<Guid>(type: "uuid", nullable: true),
                BriefId = table.Column<Guid>(type: "uuid", nullable: true),
                NoteId = table.Column<Guid>(type: "uuid", nullable: true),
                ApprovalStatus = table.Column<int>(type: "integer", nullable: false),
                SupersedesItemId = table.Column<Guid>(type: "uuid", nullable: true),
                CreatedByEmployeeId = table.Column<Guid>(type: "uuid", nullable: true),
                CreatedByContactId = table.Column<Guid>(type: "uuid", nullable: true),
                ProjectTaskId = table.Column<Guid>(type: "uuid", nullable: true),
                IsActive = table.Column<bool>(type: "boolean", nullable: false),
                CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_crm_requirement_items", x => x.Id);
                table.ForeignKey(
                    name: "FK_crm_requirement_items_crm_contacts_CreatedByContactId",
                    column: x => x.CreatedByContactId,
                    principalSchema: "crm",
                    principalTable: "crm_contacts",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_crm_requirement_items_crm_requirement_items_SupersedesItemId",
                    column: x => x.SupersedesItemId,
                    principalSchema: "crm",
                    principalTable: "crm_requirement_items",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_crm_requirement_items_crm_requirement_periods_PeriodId",
                    column: x => x.PeriodId,
                    principalSchema: "crm",
                    principalTable: "crm_requirement_periods",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "FK_crm_requirement_items_crm_space_types_SpaceTypeId",
                    column: x => x.SpaceTypeId,
                    principalSchema: "crm",
                    principalTable: "crm_space_types",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "crm_lead_intakes",
            schema: "crm",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                LeadId = table.Column<Guid>(type: "uuid", nullable: false),
                JobTitle = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                ClientAddress = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                Sector = table.Column<int>(type: "integer", nullable: true),
                UnitAddress = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                AreaSqm = table.Column<decimal>(type: "numeric", nullable: true),
                UsedDesignerBefore = table.Column<bool>(type: "boolean", nullable: true),
                DesignerExperience = table.Column<int>(type: "integer", nullable: true),
                DesignerExperienceReason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                IsActive = table.Column<bool>(type: "boolean", nullable: false),
                CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_crm_lead_intakes", x => x.Id);
                table.ForeignKey(
                    name: "FK_crm_lead_intakes_crm_leads_LeadId",
                    column: x => x.LeadId,
                    principalSchema: "crm",
                    principalTable: "crm_leads",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "crm_catalog_attribute_options",
            schema: "crm",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                AttributeId = table.Column<Guid>(type: "uuid", nullable: false),
                Code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                NameEn = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                NameAr = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                ImageUri = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                SortOrder = table.Column<int>(type: "integer", nullable: false),
                IsActive = table.Column<bool>(type: "boolean", nullable: false),
                CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_crm_catalog_attribute_options", x => x.Id);
                table.ForeignKey(
                    name: "FK_crm_catalog_attribute_options_crm_catalog_attributes_Attrib~",
                    column: x => x.AttributeId,
                    principalSchema: "crm",
                    principalTable: "crm_catalog_attributes",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "crm_brief_spaces",
            schema: "crm",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                BriefId = table.Column<Guid>(type: "uuid", nullable: false),
                SpaceId = table.Column<Guid>(type: "uuid", nullable: true),
                CountOrPieces = table.Column<int>(type: "integer", nullable: true),
                Requirements = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                MeetingId = table.Column<Guid>(type: "uuid", nullable: true),
                ApprovalStatus = table.Column<int>(type: "integer", nullable: false),
                SupersedesSpaceId = table.Column<Guid>(type: "uuid", nullable: true),
                ProjectTaskId = table.Column<Guid>(type: "uuid", nullable: true),
                CreatedById = table.Column<Guid>(type: "uuid", nullable: false),
                IsActive = table.Column<bool>(type: "boolean", nullable: false),
                CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_crm_brief_spaces", x => x.Id);
                table.ForeignKey(
                    name: "FK_crm_brief_spaces_crm_brief_spaces_SupersedesSpaceId",
                    column: x => x.SupersedesSpaceId,
                    principalSchema: "crm",
                    principalTable: "crm_brief_spaces",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_crm_brief_spaces_crm_design_briefs_BriefId",
                    column: x => x.BriefId,
                    principalSchema: "crm",
                    principalTable: "crm_design_briefs",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "FK_crm_brief_spaces_crm_spaces_SpaceId",
                    column: x => x.SpaceId,
                    principalSchema: "crm",
                    principalTable: "crm_spaces",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "crm_meeting_slots",
            schema: "crm",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                MeetingId = table.Column<Guid>(type: "uuid", nullable: false),
                StartsAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                EndsAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                Status = table.Column<int>(type: "integer", nullable: false),
                ProposedByEmployeeId = table.Column<Guid>(type: "uuid", nullable: true),
                ProposedByContactId = table.Column<Guid>(type: "uuid", nullable: true),
                IsActive = table.Column<bool>(type: "boolean", nullable: false),
                CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_crm_meeting_slots", x => x.Id);
                table.ForeignKey(
                    name: "FK_crm_meeting_slots_crm_contacts_ProposedByContactId",
                    column: x => x.ProposedByContactId,
                    principalSchema: "crm",
                    principalTable: "crm_contacts",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_crm_meeting_slots_crm_meetings_MeetingId",
                    column: x => x.MeetingId,
                    principalSchema: "crm",
                    principalTable: "crm_meetings",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "crm_artifact_documents",
            schema: "crm",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                DocumentId = table.Column<Guid>(type: "uuid", nullable: false),
                BriefId = table.Column<Guid>(type: "uuid", nullable: true),
                SpaceId = table.Column<Guid>(type: "uuid", nullable: true),
                RequirementItemId = table.Column<Guid>(type: "uuid", nullable: true),
                MeetingId = table.Column<Guid>(type: "uuid", nullable: true),
                AddedByEmployeeId = table.Column<Guid>(type: "uuid", nullable: true),
                AddedByContactId = table.Column<Guid>(type: "uuid", nullable: true),
                IsActive = table.Column<bool>(type: "boolean", nullable: false),
                CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_crm_artifact_documents", x => x.Id);
                table.ForeignKey(
                    name: "FK_crm_artifact_documents_crm_contacts_AddedByContactId",
                    column: x => x.AddedByContactId,
                    principalSchema: "crm",
                    principalTable: "crm_contacts",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_crm_artifact_documents_crm_design_briefs_BriefId",
                    column: x => x.BriefId,
                    principalSchema: "crm",
                    principalTable: "crm_design_briefs",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "FK_crm_artifact_documents_crm_meetings_MeetingId",
                    column: x => x.MeetingId,
                    principalSchema: "crm",
                    principalTable: "crm_meetings",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "FK_crm_artifact_documents_crm_requirement_items_RequirementIte~",
                    column: x => x.RequirementItemId,
                    principalSchema: "crm",
                    principalTable: "crm_requirement_items",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "FK_crm_artifact_documents_crm_spaces_SpaceId",
                    column: x => x.SpaceId,
                    principalSchema: "crm",
                    principalTable: "crm_spaces",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "crm_lead_intake_residentials",
            schema: "crm",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                LeadIntakeId = table.Column<Guid>(type: "uuid", nullable: false),
                UnitType = table.Column<int>(type: "integer", nullable: true),
                UnitCondition = table.Column<int>(type: "integer", nullable: true),
                IsLicensed = table.Column<bool>(type: "boolean", nullable: true),
                IsRented = table.Column<bool>(type: "boolean", nullable: true),
                FinishingPurpose = table.Column<int>(type: "integer", nullable: true),
                IsActive = table.Column<bool>(type: "boolean", nullable: false),
                CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_crm_lead_intake_residentials", x => x.Id);
                table.ForeignKey(
                    name: "FK_crm_lead_intake_residentials_crm_lead_intakes_LeadIntakeId",
                    column: x => x.LeadIntakeId,
                    principalSchema: "crm",
                    principalTable: "crm_lead_intakes",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "crm_catalog_item_attribute_values",
            schema: "crm",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                ItemId = table.Column<Guid>(type: "uuid", nullable: false),
                AttributeId = table.Column<Guid>(type: "uuid", nullable: false),
                ValueText = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                ValueNumber = table.Column<decimal>(type: "numeric(18,4)", nullable: true),
                ValueBool = table.Column<bool>(type: "boolean", nullable: true),
                ValueDate = table.Column<DateOnly>(type: "date", nullable: true),
                OptionId = table.Column<Guid>(type: "uuid", nullable: true),
                IsActive = table.Column<bool>(type: "boolean", nullable: false),
                CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_crm_catalog_item_attribute_values", x => x.Id);
                table.ForeignKey(
                    name: "FK_crm_catalog_item_attribute_values_crm_catalog_attribute_opt~",
                    column: x => x.OptionId,
                    principalSchema: "crm",
                    principalTable: "crm_catalog_attribute_options",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_crm_catalog_item_attribute_values_crm_catalog_attributes_At~",
                    column: x => x.AttributeId,
                    principalSchema: "crm",
                    principalTable: "crm_catalog_attributes",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_crm_catalog_item_attribute_values_crm_catalog_items_ItemId",
                    column: x => x.ItemId,
                    principalSchema: "crm",
                    principalTable: "crm_catalog_items",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "crm_space_items",
            schema: "crm",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                SpaceId = table.Column<Guid>(type: "uuid", nullable: false),
                CatalogItemId = table.Column<Guid>(type: "uuid", nullable: true),
                CustomName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                CustomCategoryId = table.Column<Guid>(type: "uuid", nullable: true),
                Status = table.Column<int>(type: "integer", nullable: false),
                Note = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                SourceBriefSpaceId = table.Column<Guid>(type: "uuid", nullable: true),
                IsActive = table.Column<bool>(type: "boolean", nullable: false),
                CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_crm_space_items", x => x.Id);
                table.ForeignKey(
                    name: "FK_crm_space_items_crm_brief_spaces_SourceBriefSpaceId",
                    column: x => x.SourceBriefSpaceId,
                    principalSchema: "crm",
                    principalTable: "crm_brief_spaces",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.SetNull);
                table.ForeignKey(
                    name: "FK_crm_space_items_crm_catalog_categories_CustomCategoryId",
                    column: x => x.CustomCategoryId,
                    principalSchema: "crm",
                    principalTable: "crm_catalog_categories",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_crm_space_items_crm_catalog_items_CatalogItemId",
                    column: x => x.CatalogItemId,
                    principalSchema: "crm",
                    principalTable: "crm_catalog_items",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_crm_space_items_crm_spaces_SpaceId",
                    column: x => x.SpaceId,
                    principalSchema: "crm",
                    principalTable: "crm_spaces",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "crm_space_item_attribute_values",
            schema: "crm",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                SpaceItemId = table.Column<Guid>(type: "uuid", nullable: false),
                AttributeId = table.Column<Guid>(type: "uuid", nullable: false),
                ValueText = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                ValueNumber = table.Column<decimal>(type: "numeric(18,4)", nullable: true),
                ValueBool = table.Column<bool>(type: "boolean", nullable: true),
                ValueDate = table.Column<DateOnly>(type: "date", nullable: true),
                OptionId = table.Column<Guid>(type: "uuid", nullable: true),
                IsActive = table.Column<bool>(type: "boolean", nullable: false),
                CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_crm_space_item_attribute_values", x => x.Id);
                table.ForeignKey(
                    name: "FK_crm_space_item_attribute_values_crm_catalog_attribute_optio~",
                    column: x => x.OptionId,
                    principalSchema: "crm",
                    principalTable: "crm_catalog_attribute_options",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_crm_space_item_attribute_values_crm_catalog_attributes_Attr~",
                    column: x => x.AttributeId,
                    principalSchema: "crm",
                    principalTable: "crm_catalog_attributes",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_crm_space_item_attribute_values_crm_space_items_SpaceItemId",
                    column: x => x.SpaceItemId,
                    principalSchema: "crm",
                    principalTable: "crm_space_items",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_clients_AppUserId",
            schema: "crm",
            table: "clients",
            column: "AppUserId");

        migrationBuilder.CreateIndex(
            name: "IX_crm_artifact_documents_AddedByContactId",
            schema: "crm",
            table: "crm_artifact_documents",
            column: "AddedByContactId");

        migrationBuilder.CreateIndex(
            name: "IX_crm_artifact_documents_BriefId",
            schema: "crm",
            table: "crm_artifact_documents",
            column: "BriefId");

        migrationBuilder.CreateIndex(
            name: "IX_crm_artifact_documents_DocumentId",
            schema: "crm",
            table: "crm_artifact_documents",
            column: "DocumentId");

        migrationBuilder.CreateIndex(
            name: "IX_crm_artifact_documents_MeetingId",
            schema: "crm",
            table: "crm_artifact_documents",
            column: "MeetingId");

        migrationBuilder.CreateIndex(
            name: "IX_crm_artifact_documents_RequirementItemId",
            schema: "crm",
            table: "crm_artifact_documents",
            column: "RequirementItemId");

        migrationBuilder.CreateIndex(
            name: "IX_crm_artifact_documents_SpaceId",
            schema: "crm",
            table: "crm_artifact_documents",
            column: "SpaceId");

        migrationBuilder.CreateIndex(
            name: "IX_crm_brief_head_snapshots_BriefId_Round",
            schema: "crm",
            table: "crm_brief_head_snapshots",
            columns: new[] { "BriefId", "Round" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_crm_brief_spaces_ApprovalStatus",
            schema: "crm",
            table: "crm_brief_spaces",
            column: "ApprovalStatus");

        migrationBuilder.CreateIndex(
            name: "IX_crm_brief_spaces_BriefId",
            schema: "crm",
            table: "crm_brief_spaces",
            column: "BriefId");

        migrationBuilder.CreateIndex(
            name: "IX_crm_brief_spaces_SpaceId",
            schema: "crm",
            table: "crm_brief_spaces",
            column: "SpaceId",
            filter: "\"SpaceId\" IS NOT NULL");

        migrationBuilder.CreateIndex(
            name: "IX_crm_brief_spaces_SupersedesSpaceId",
            schema: "crm",
            table: "crm_brief_spaces",
            column: "SupersedesSpaceId");

        migrationBuilder.CreateIndex(
            name: "IX_crm_catalog_attribute_options_AttributeId",
            schema: "crm",
            table: "crm_catalog_attribute_options",
            column: "AttributeId");

        migrationBuilder.CreateIndex(
            name: "IX_crm_catalog_attribute_options_AttributeId_Code",
            schema: "crm",
            table: "crm_catalog_attribute_options",
            columns: new[] { "AttributeId", "Code" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_crm_catalog_attributes_CategoryId",
            schema: "crm",
            table: "crm_catalog_attributes",
            column: "CategoryId");

        migrationBuilder.CreateIndex(
            name: "IX_crm_catalog_attributes_CategoryId_Code",
            schema: "crm",
            table: "crm_catalog_attributes",
            columns: new[] { "CategoryId", "Code" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_crm_catalog_categories_Code",
            schema: "crm",
            table: "crm_catalog_categories",
            column: "Code",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_crm_catalog_categories_ParentId",
            schema: "crm",
            table: "crm_catalog_categories",
            column: "ParentId",
            filter: "\"ParentId\" IS NOT NULL");

        migrationBuilder.CreateIndex(
            name: "IX_crm_catalog_categories_Path",
            schema: "crm",
            table: "crm_catalog_categories",
            column: "Path");

        migrationBuilder.CreateIndex(
            name: "IX_crm_catalog_category_space_types_CategoryId_SpaceTypeId",
            schema: "crm",
            table: "crm_catalog_category_space_types",
            columns: new[] { "CategoryId", "SpaceTypeId" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_crm_catalog_category_space_types_SpaceTypeId",
            schema: "crm",
            table: "crm_catalog_category_space_types",
            column: "SpaceTypeId");

        migrationBuilder.CreateIndex(
            name: "IX_crm_catalog_item_attribute_values_AttributeId",
            schema: "crm",
            table: "crm_catalog_item_attribute_values",
            column: "AttributeId");

        migrationBuilder.CreateIndex(
            name: "IX_crm_catalog_item_attribute_values_ItemId_AttributeId",
            schema: "crm",
            table: "crm_catalog_item_attribute_values",
            columns: new[] { "ItemId", "AttributeId" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_crm_catalog_item_attribute_values_OptionId",
            schema: "crm",
            table: "crm_catalog_item_attribute_values",
            column: "OptionId");

        migrationBuilder.CreateIndex(
            name: "IX_crm_catalog_items_CategoryId",
            schema: "crm",
            table: "crm_catalog_items",
            column: "CategoryId");

        migrationBuilder.CreateIndex(
            name: "IX_crm_catalog_items_Code",
            schema: "crm",
            table: "crm_catalog_items",
            column: "Code",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_crm_client_notes_ClientId",
            schema: "crm",
            table: "crm_client_notes",
            column: "ClientId");

        migrationBuilder.CreateIndex(
            name: "IX_crm_client_notes_ProjectId",
            schema: "crm",
            table: "crm_client_notes",
            column: "ProjectId",
            filter: "\"ProjectId\" IS NOT NULL");

        migrationBuilder.CreateIndex(
            name: "ux_crm_contacts_client_primary",
            schema: "crm",
            table: "crm_contacts",
            column: "ClientId",
            unique: true,
            filter: "\"IsPrimary\" = true");

        migrationBuilder.CreateIndex(
            name: "IX_crm_design_briefs_ProjectId",
            schema: "crm",
            table: "crm_design_briefs",
            column: "ProjectId");

        migrationBuilder.CreateIndex(
            name: "IX_crm_design_briefs_ProjectId_ProjectStageId",
            schema: "crm",
            table: "crm_design_briefs",
            columns: new[] { "ProjectId", "ProjectStageId" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_crm_design_briefs_ProjectStageId",
            schema: "crm",
            table: "crm_design_briefs",
            column: "ProjectStageId",
            filter: "\"ProjectStageId\" IS NOT NULL");

        migrationBuilder.CreateIndex(
            name: "IX_crm_lead_intake_residentials_LeadIntakeId",
            schema: "crm",
            table: "crm_lead_intake_residentials",
            column: "LeadIntakeId",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_crm_lead_intakes_LeadId",
            schema: "crm",
            table: "crm_lead_intakes",
            column: "LeadId",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_crm_leads_ConvertedClientId",
            schema: "crm",
            table: "crm_leads",
            column: "ConvertedClientId",
            filter: "\"ConvertedClientId\" IS NOT NULL");

        migrationBuilder.CreateIndex(
            name: "IX_crm_leads_OwnerId",
            schema: "crm",
            table: "crm_leads",
            column: "OwnerId");

        migrationBuilder.CreateIndex(
            name: "IX_crm_leads_Status",
            schema: "crm",
            table: "crm_leads",
            column: "Status");

        migrationBuilder.CreateIndex(
            name: "IX_crm_meeting_slots_ProposedByContactId",
            schema: "crm",
            table: "crm_meeting_slots",
            column: "ProposedByContactId");

        migrationBuilder.CreateIndex(
            name: "ux_crm_meeting_slots_meeting_accepted",
            schema: "crm",
            table: "crm_meeting_slots",
            column: "MeetingId",
            unique: true,
            filter: "\"Status\" = 2");

        migrationBuilder.CreateIndex(
            name: "IX_crm_meetings_PeriodId",
            schema: "crm",
            table: "crm_meetings",
            column: "PeriodId",
            filter: "\"PeriodId\" IS NOT NULL");

        migrationBuilder.CreateIndex(
            name: "IX_crm_meetings_ProjectId",
            schema: "crm",
            table: "crm_meetings",
            column: "ProjectId");

        migrationBuilder.CreateIndex(
            name: "IX_crm_meetings_RequestedByContactId",
            schema: "crm",
            table: "crm_meetings",
            column: "RequestedByContactId");

        migrationBuilder.CreateIndex(
            name: "IX_crm_meetings_Status",
            schema: "crm",
            table: "crm_meetings",
            column: "Status");

        migrationBuilder.CreateIndex(
            name: "IX_crm_requirement_items_ApprovalStatus",
            schema: "crm",
            table: "crm_requirement_items",
            column: "ApprovalStatus");

        migrationBuilder.CreateIndex(
            name: "IX_crm_requirement_items_CreatedByContactId",
            schema: "crm",
            table: "crm_requirement_items",
            column: "CreatedByContactId");

        migrationBuilder.CreateIndex(
            name: "IX_crm_requirement_items_PeriodId",
            schema: "crm",
            table: "crm_requirement_items",
            column: "PeriodId");

        migrationBuilder.CreateIndex(
            name: "IX_crm_requirement_items_ProjectId",
            schema: "crm",
            table: "crm_requirement_items",
            column: "ProjectId");

        migrationBuilder.CreateIndex(
            name: "IX_crm_requirement_items_SpaceTypeId",
            schema: "crm",
            table: "crm_requirement_items",
            column: "SpaceTypeId");

        migrationBuilder.CreateIndex(
            name: "IX_crm_requirement_items_SupersedesItemId",
            schema: "crm",
            table: "crm_requirement_items",
            column: "SupersedesItemId");

        migrationBuilder.CreateIndex(
            name: "IX_crm_requirement_periods_ProjectStageId",
            schema: "crm",
            table: "crm_requirement_periods",
            column: "ProjectStageId",
            filter: "\"ProjectStageId\" IS NOT NULL");

        migrationBuilder.CreateIndex(
            name: "ux_crm_requirement_periods_open_per_project",
            schema: "crm",
            table: "crm_requirement_periods",
            column: "ProjectId",
            unique: true,
            filter: "\"Status\" = 1");

        migrationBuilder.CreateIndex(
            name: "IX_crm_space_item_attribute_values_AttributeId",
            schema: "crm",
            table: "crm_space_item_attribute_values",
            column: "AttributeId");

        migrationBuilder.CreateIndex(
            name: "IX_crm_space_item_attribute_values_OptionId",
            schema: "crm",
            table: "crm_space_item_attribute_values",
            column: "OptionId");

        migrationBuilder.CreateIndex(
            name: "IX_crm_space_item_attribute_values_SpaceItemId_AttributeId",
            schema: "crm",
            table: "crm_space_item_attribute_values",
            columns: new[] { "SpaceItemId", "AttributeId" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_crm_space_items_CatalogItemId",
            schema: "crm",
            table: "crm_space_items",
            column: "CatalogItemId");

        migrationBuilder.CreateIndex(
            name: "IX_crm_space_items_CustomCategoryId",
            schema: "crm",
            table: "crm_space_items",
            column: "CustomCategoryId");

        migrationBuilder.CreateIndex(
            name: "IX_crm_space_items_SourceBriefSpaceId",
            schema: "crm",
            table: "crm_space_items",
            column: "SourceBriefSpaceId");

        migrationBuilder.CreateIndex(
            name: "IX_crm_space_items_SpaceId",
            schema: "crm",
            table: "crm_space_items",
            column: "SpaceId");

        migrationBuilder.CreateIndex(
            name: "ux_crm_space_items_space_item",
            schema: "crm",
            table: "crm_space_items",
            columns: new[] { "SpaceId", "CatalogItemId" },
            unique: true,
            filter: "\"CatalogItemId\" IS NOT NULL AND \"IsActive\" = true");

        migrationBuilder.CreateIndex(
            name: "IX_crm_space_types_Code",
            schema: "crm",
            table: "crm_space_types",
            column: "Code",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_crm_spaces_ProjectId",
            schema: "crm",
            table: "crm_spaces",
            column: "ProjectId");

        migrationBuilder.CreateIndex(
            name: "IX_crm_spaces_SpaceTypeId",
            schema: "crm",
            table: "crm_spaces",
            column: "SpaceTypeId");

        migrationBuilder.CreateIndex(
            name: "ux_crm_spaces_project_name",
            schema: "crm",
            table: "crm_spaces",
            columns: new[] { "ProjectId", "Name" },
            unique: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "crm_artifact_documents",
            schema: "crm");

        migrationBuilder.DropTable(
            name: "crm_brief_head_snapshots",
            schema: "crm");

        migrationBuilder.DropTable(
            name: "crm_catalog_category_space_types",
            schema: "crm");

        migrationBuilder.DropTable(
            name: "crm_catalog_item_attribute_values",
            schema: "crm");

        migrationBuilder.DropTable(
            name: "crm_client_notes",
            schema: "crm");

        migrationBuilder.DropTable(
            name: "crm_lead_intake_residentials",
            schema: "crm");

        migrationBuilder.DropTable(
            name: "crm_meeting_slots",
            schema: "crm");

        migrationBuilder.DropTable(
            name: "crm_space_item_attribute_values",
            schema: "crm");

        migrationBuilder.DropTable(
            name: "crm_requirement_items",
            schema: "crm");

        migrationBuilder.DropTable(
            name: "crm_lead_intakes",
            schema: "crm");

        migrationBuilder.DropTable(
            name: "crm_meetings",
            schema: "crm");

        migrationBuilder.DropTable(
            name: "crm_catalog_attribute_options",
            schema: "crm");

        migrationBuilder.DropTable(
            name: "crm_space_items",
            schema: "crm");

        migrationBuilder.DropTable(
            name: "crm_leads",
            schema: "crm");

        migrationBuilder.DropTable(
            name: "crm_contacts",
            schema: "crm");

        migrationBuilder.DropTable(
            name: "crm_requirement_periods",
            schema: "crm");

        migrationBuilder.DropTable(
            name: "crm_catalog_attributes",
            schema: "crm");

        migrationBuilder.DropTable(
            name: "crm_brief_spaces",
            schema: "crm");

        migrationBuilder.DropTable(
            name: "crm_catalog_items",
            schema: "crm");

        migrationBuilder.DropTable(
            name: "clients",
            schema: "crm");

        migrationBuilder.DropTable(
            name: "crm_design_briefs",
            schema: "crm");

        migrationBuilder.DropTable(
            name: "crm_spaces",
            schema: "crm");

        migrationBuilder.DropTable(
            name: "crm_catalog_categories",
            schema: "crm");

        migrationBuilder.DropTable(
            name: "crm_space_types",
            schema: "crm");
    }
}
