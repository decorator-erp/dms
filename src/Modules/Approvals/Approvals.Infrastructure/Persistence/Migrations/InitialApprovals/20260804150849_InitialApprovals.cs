using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Approvals.Infrastructure.Persistence.Migrations.InitialApprovals
{
    /// <inheritdoc />
    public partial class InitialApprovals : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "approvals");

            migrationBuilder.CreateTable(
                name: "approvals",
                schema: "approvals",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SubjectType = table.Column<int>(type: "integer", nullable: false),
                    SubjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    Type = table.Column<int>(type: "integer", nullable: false),
                    RequestedByUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false),
                    Decision = table.Column<int>(type: "integer", nullable: true),
                    DecidedByUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: true),
                    Comment = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    Round = table.Column<int>(type: "integer", nullable: false),
                    DueAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    DecidedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_approvals", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "approval_approvers",
                schema: "approvals",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Kind = table.Column<int>(type: "integer", nullable: false),
                    ApproverUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false),
                    approval_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_approval_approvers", x => x.id);
                    table.ForeignKey(
                        name: "FK_approval_approvers_approvals_approval_id",
                        column: x => x.approval_id,
                        principalSchema: "approvals",
                        principalTable: "approvals",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_approval_approvers_approval_id",
                schema: "approvals",
                table: "approval_approvers",
                column: "approval_id");

            migrationBuilder.CreateIndex(
                name: "IX_approvals_RequestedByUserId",
                schema: "approvals",
                table: "approvals",
                column: "RequestedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_approvals_SubjectType_SubjectId",
                schema: "approvals",
                table: "approvals",
                columns: new[] { "SubjectType", "SubjectId" });

            // Cross-schema FK - EF cannot emit this itself (identity.users and approvals.approvals are in
            // two different EF models), so it's written by hand, same pattern as hr.employees' FK to
            // identity.users (see that migration's own comment). ON DELETE RESTRICT, never CASCADE, across
            // a module boundary (docs/cross-schema-foreign-keys.md). Requires identity.users to already
            // exist, which is why ModuleDependencyOrder.SchemaOrder migrates "identity" before "approvals".
            // Only RequestedByUserId gets this FK - each approval_approvers.ApproverUserId is a bare,
            // unvalidated identity user id with no FK of its own (same "unvalidated cross-module
            // reference" shape as Project.ClientId elsewhere in this plan), since an owned collection row
            // isn't its own aggregate and a per-row FK would multiply the same constraint pointlessly.
            migrationBuilder.Sql(
                """
                ALTER TABLE approvals.approvals
                    ADD CONSTRAINT fk_approvals_requested_by_user
                    FOREIGN KEY ("RequestedByUserId") REFERENCES identity.users("Id")
                    ON DELETE RESTRICT;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("ALTER TABLE approvals.approvals DROP CONSTRAINT fk_approvals_requested_by_user;");

            migrationBuilder.DropTable(
                name: "approval_approvers",
                schema: "approvals");

            migrationBuilder.DropTable(
                name: "approvals",
                schema: "approvals");
        }
    }
}
