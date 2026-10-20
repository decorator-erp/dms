using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Identity.Infrastructure.Persistence.Migrations;

/// <summary>
/// Aug 4.1: indexes for <c>RefreshTokenPruneJob</c>, which deletes rows by <c>ExpiresOn</c> and
/// <c>RevokedOn</c>. Indexes only, so no guard.
/// </summary>
public partial class RefreshTokenPruneIndexes : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateIndex(
            name: "IX_refresh_tokens_ExpiresOn",
            schema: "identity",
            table: "refresh_tokens",
            column: "ExpiresOn");

        migrationBuilder.CreateIndex(
            name: "IX_refresh_tokens_RevokedOn",
            schema: "identity",
            table: "refresh_tokens",
            column: "RevokedOn");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_refresh_tokens_ExpiresOn",
            schema: "identity",
            table: "refresh_tokens");

        migrationBuilder.DropIndex(
            name: "IX_refresh_tokens_RevokedOn",
            schema: "identity",
            table: "refresh_tokens");
    }
}
