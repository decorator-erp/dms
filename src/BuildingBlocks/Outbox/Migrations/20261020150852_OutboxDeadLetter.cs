using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BuildingBlocks.Outbox.Migrations;

/// <inheritdoc />
public partial class OutboxDeadLetter : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "ix_outbox_messages_unprocessed",
            schema: "shared",
            table: "outbox_messages");

        migrationBuilder.AddColumn<DateTimeOffset>(
            name: "dead_lettered_utc",
            schema: "shared",
            table: "outbox_messages",
            type: "timestamp with time zone",
            nullable: true);

        migrationBuilder.AddColumn<DateTimeOffset>(
            name: "next_attempt_utc",
            schema: "shared",
            table: "outbox_messages",
            type: "timestamp with time zone",
            nullable: false,
            defaultValueSql: "now()");

        // Adding a NOT NULL DEFAULT now() column fills every existing row with the migration time, so
        // the backfill keys on processed_utc, not on next_attempt_utc IS NULL (which would match
        // nothing). Pending messages keep their place: they are due from when they occurred.
        migrationBuilder.Sql(
            "UPDATE shared.outbox_messages SET next_attempt_utc = occurred_utc WHERE processed_utc IS NULL;");

        migrationBuilder.CreateIndex(
            name: "ix_outbox_messages_unprocessed",
            schema: "shared",
            table: "outbox_messages",
            columns: new[] { "next_attempt_utc", "occurred_utc" },
            filter: "processed_utc IS NULL AND dead_lettered_utc IS NULL");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "ix_outbox_messages_unprocessed",
            schema: "shared",
            table: "outbox_messages");

        migrationBuilder.DropColumn(
            name: "dead_lettered_utc",
            schema: "shared",
            table: "outbox_messages");

        migrationBuilder.DropColumn(
            name: "next_attempt_utc",
            schema: "shared",
            table: "outbox_messages");

        migrationBuilder.CreateIndex(
            name: "ix_outbox_messages_unprocessed",
            schema: "shared",
            table: "outbox_messages",
            column: "occurred_utc",
            filter: "processed_utc IS NULL");
    }
}
