using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Approvals.Infrastructure.Persistence.Migrations.InitialApprovals;

// S3: the subject's project, as the requesting module states it. Nullable and unconstrained (a bare cross-module
// id, no FK); rows made before this migration stay null.
/// <inheritdoc />
public partial class AddApprovalProjectId : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.AddColumn<Guid>(
            name: "ProjectId",
            schema: "approvals",
            table: "approvals",
            type: "uuid",
            nullable: true);

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.DropColumn(
            name: "ProjectId",
            schema: "approvals",
            table: "approvals");
}
