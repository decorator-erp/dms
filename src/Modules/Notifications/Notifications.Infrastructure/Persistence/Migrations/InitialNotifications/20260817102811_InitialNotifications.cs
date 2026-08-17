using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Notifications.Infrastructure.Persistence.Migrations.InitialNotifications
{
    /// <inheritdoc />
    public partial class InitialNotifications : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "notify");

            migrationBuilder.CreateTable(
                name: "activities",
                schema: "notify",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ActorUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: true),
                    ActorName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    Action = table.Column<int>(type: "integer", nullable: false),
                    EntityName = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    EntityId = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    EntityDisplayName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: true),
                    Message = table.Column<string>(type: "text", nullable: false),
                    OccurredAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_activities", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "notifications",
                schema: "notify",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RecipientUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false),
                    Type = table.Column<int>(type: "integer", nullable: false),
                    Title = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Body = table.Column<string>(type: "text", nullable: true),
                    EntityType = table.Column<int>(type: "integer", nullable: true),
                    EntityId = table.Column<Guid>(type: "uuid", nullable: true),
                    ReadAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DedupeKey = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_notifications", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_activities_entity",
                schema: "notify",
                table: "activities",
                columns: new[] { "EntityName", "EntityId", "OccurredAtUtc" },
                descending: new[] { false, false, true });

            migrationBuilder.CreateIndex(
                name: "ix_activities_project",
                schema: "notify",
                table: "activities",
                columns: new[] { "ProjectId", "OccurredAtUtc" },
                descending: new[] { false, true },
                filter: "\"ProjectId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_notifications_recipient_read",
                schema: "notify",
                table: "notifications",
                columns: new[] { "RecipientUserId", "ReadAtUtc" });

            migrationBuilder.CreateIndex(
                name: "ux_notifications_dedupe_key",
                schema: "notify",
                table: "notifications",
                column: "DedupeKey",
                unique: true,
                filter: "\"DedupeKey\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "activities",
                schema: "notify");

            migrationBuilder.DropTable(
                name: "notifications",
                schema: "notify");
        }
    }
}
