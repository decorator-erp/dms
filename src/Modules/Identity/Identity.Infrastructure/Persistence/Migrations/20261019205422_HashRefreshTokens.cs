using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Identity.Infrastructure.Persistence.Migrations;

/// <summary>
/// K5: refresh tokens are stored as their SHA-256 (lower-case hex), never as the token itself.
///
/// <para><b>This migration expires every existing refresh token.</b> The stored values were the plain
/// tokens. They are hashed in place (so the unique index still holds, and no plain value survives the
/// migration), and every row's <c>ExpiresOn</c> is moved to the migration's time. <b>Every user signs
/// in again after this deploys.</b> Expiring rather than migrating them is deliberate: a token that
/// sat in plaintext may already have been copied, and this is the moment to retire it.</para>
///
/// <para>Hand-written, not the scaffold: the scaffold drops the column and adds a new one defaulting to
/// <c>''</c>, which the new unique index rejects as soon as two rows exist.</para>
///
/// <para>It also turns on <c>LockoutEnabled</c> for every user (K4). <c>UserManager.CreateAsync</c> has
/// always set it, so this should change nothing; it guarantees no older row is exempt from lockout.</para>
/// </summary>
public partial class HashRefreshTokens : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_refresh_tokens_Token",
            schema: "identity",
            table: "refresh_tokens");

        migrationBuilder.RenameColumn(
            name: "Token",
            schema: "identity",
            table: "refresh_tokens",
            newName: "TokenHash");

        migrationBuilder.Sql("""
            UPDATE identity.refresh_tokens
            SET "TokenHash" = encode(sha256(convert_to("TokenHash", 'UTF8')), 'hex'),
                "ExpiresOn" = LEAST("ExpiresOn", now());
            """);

        migrationBuilder.AlterColumn<string>(
            name: "TokenHash",
            schema: "identity",
            table: "refresh_tokens",
            type: "character varying(64)",
            maxLength: 64,
            nullable: false,
            oldClrType: typeof(string),
            oldType: "character varying(256)",
            oldMaxLength: 256);

        migrationBuilder.CreateIndex(
            name: "IX_refresh_tokens_TokenHash",
            schema: "identity",
            table: "refresh_tokens",
            column: "TokenHash",
            unique: true);

        migrationBuilder.Sql("""UPDATE identity.users SET "LockoutEnabled" = true WHERE NOT "LockoutEnabled";""");
    }

    /// <summary>
    /// Restores the column's name, width and index. The tokens themselves cannot come back (only their
    /// hashes were kept, and every row was expired), so after a rollback every user signs in again too.
    /// <c>LockoutEnabled</c> is left on.
    /// </summary>
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_refresh_tokens_TokenHash",
            schema: "identity",
            table: "refresh_tokens");

        migrationBuilder.AlterColumn<string>(
            name: "TokenHash",
            schema: "identity",
            table: "refresh_tokens",
            type: "character varying(256)",
            maxLength: 256,
            nullable: false,
            oldClrType: typeof(string),
            oldType: "character varying(64)",
            oldMaxLength: 64);

        migrationBuilder.RenameColumn(
            name: "TokenHash",
            schema: "identity",
            table: "refresh_tokens",
            newName: "Token");

        migrationBuilder.CreateIndex(
            name: "IX_refresh_tokens_Token",
            schema: "identity",
            table: "refresh_tokens",
            column: "Token",
            unique: true);
    }
}
