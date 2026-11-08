using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Notifications.Infrastructure.Persistence.Migrations.InitialNotifications;

/// <inheritdoc />
public partial class AddNotificationProjectId : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.AddColumn<Guid>(
            name: "ProjectId",
            schema: "notify",
            table: "notifications",
            type: "uuid",
            nullable: true);

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.DropColumn(
            name: "ProjectId",
            schema: "notify",
            table: "notifications");
}
