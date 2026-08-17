using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Notifications.Infrastructure.Persistence.Migrations.InitialNotifications
{
    /// <summary>
    /// Notifications' two cross-schema foreign keys, both <c>notify → identity</c>. EF cannot emit
    /// either — the two entities are in different models — so each is raw SQL, with quoted PascalCase
    /// columns and <c>ON DELETE RESTRICT</c>, matching every other cross-schema FK in the solution.
    ///
    /// <para><b>Both are legal because notify migrates LAST</b>, and <c>identity</c> is second. The
    /// pair <c>("notify", "identity")</c> was already in
    /// <c>Rule9CrossSchemaForeignKeyTests.AllowedPairs</c> and already listed in
    /// <c>docs/cross-schema-foreign-keys.md</c> two phases before this module existed; that file also
    /// already placed <c>notify</c> last in the migration order. This migration carries out a plan
    /// rather than making a new decision.</para>
    ///
    /// <para><b>RESTRICT, where the backend cascades.</b> The backend's
    /// <c>notifications.recipient_user_id</c> is <c>Cascade</c>. Arch rule 9 forbids CASCADE across a
    /// schema boundary — a delete in one module silently destroying another module's rows is exactly
    /// what that rule exists to prevent — so this diverges deliberately. Practical effect: a user with
    /// notifications must have them removed before the account row can be deleted. Since deleting a
    /// user is not a path this system models at all (deactivation is — see
    /// <c>EmployeeDeactivatedIntegrationEventHandler</c>), the constraint costs nothing real and buys
    /// the guarantee that no notification outlives its recipient as an orphan.</para>
    ///
    /// <para><b>Raw SQL is invisible to a probe migration</b>, which diffs the EF model against the
    /// snapshot and cannot see a constraint the model never knew about. Dropping either of these would
    /// leave the probe empty and the build green, which is why
    /// <c>NotificationsSchemaObjectTests</c> names both against <c>pg_constraint</c> — R3's rule,
    /// established by <c>SitesSchemaObjectTests</c>.</para>
    /// </summary>
    public partial class NotificationsCrossSchemaForeignKeys : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                ALTER TABLE notify.notifications
                    ADD CONSTRAINT fk_notifications_recipient_user
                    FOREIGN KEY ("RecipientUserId") REFERENCES identity.users ("Id")
                    ON DELETE RESTRICT;
                """);

            migrationBuilder.Sql("""
                ALTER TABLE notify.activities
                    ADD CONSTRAINT fk_activities_actor_user
                    FOREIGN KEY ("ActorUserId") REFERENCES identity.users ("Id")
                    ON DELETE RESTRICT;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                ALTER TABLE notify.activities DROP CONSTRAINT fk_activities_actor_user;
                """);

            migrationBuilder.Sql("""
                ALTER TABLE notify.notifications DROP CONSTRAINT fk_notifications_recipient_user;
                """);
        }
    }
}
