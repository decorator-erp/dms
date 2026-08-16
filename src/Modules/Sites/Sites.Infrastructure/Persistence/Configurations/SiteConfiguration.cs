using BuildingBlocks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sites.Domain;

namespace Sites.Infrastructure.Persistence.Configurations;

/// <summary>
/// Every check-constraint body below quotes its columns in PascalCase. The backend's originals are
/// written in snake_case because it maps every column explicitly; this solution has no such convention,
/// so an unquoted <c>status</c> would fold to a lowercase identifier Postgres cannot resolve. See
/// <see cref="SitesDbContext"/>'s remarks.
/// </summary>
internal sealed class SiteConfiguration : IEntityTypeConfiguration<Site>
{
    public void Configure(EntityTypeBuilder<Site> builder)
    {
        // Soft delete. Raw SQL inherits none of this, which is why the two hand-written reports at
        // S1-06 re-state the guard as an explicit join condition.
        builder.HasQueryFilter(x => x.IsActive);

        builder.ToTable("sites", t =>
        {
            t.HasCheckConstraint("ck_sites_status_range", "\"Status\" BETWEEN 1 AND 4");

            t.HasCheckConstraint("ck_sites_enum_ranges", """
                    ("UnitType"       IS NULL OR "UnitType"       BETWEEN 1 AND 4)
                AND ("UnitCondition"  IS NULL OR "UnitCondition"  BETWEEN 1 AND 3)
                AND ("BuildingKind"   IS NULL OR "BuildingKind"   BETWEEN 1 AND 2)
                AND ("Position"       IS NULL OR "Position"       BETWEEN 1 AND 2)
                AND ("ElevatorStatus" IS NULL OR "ElevatorStatus" BETWEEN 1 AND 3)
                """);

            // {5,} not {5}: the D5 in the code format string is a minimum display width, not a cap.
            t.HasCheckConstraint("ck_sites_code_format", "\"Code\" ~ '^SITE-[0-9]{5,}$'");

            t.HasCheckConstraint("ck_sites_building_numbers", """
                    ("AreaSqm"       IS NULL OR "AreaSqm" > 0)
                AND ("FloorNo"       IS NULL OR "FloorNo"       BETWEEN -5 AND 200)
                AND ("UnitsPerFloor" IS NULL OR "UnitsPerFloor" BETWEEN 1 AND 100)
                """);

            // Sites are not a subject of the Approvals module, so the lifecycle trail lives on the row
            // itself. This is what keeps who/when coherent with the status they belong to -- and it is
            // why Site.ReopenForRework must clear all four stamps rather than only the decision.
            t.HasCheckConstraint("ck_sites_status_stamps", """
                   ("Status" = 1      AND "SubmittedAtUtc" IS NULL
                                      AND "DecidedAtUtc" IS NULL AND "DecidedById" IS NULL)
                OR ("Status" = 2      AND "SubmittedAtUtc" IS NOT NULL
                                      AND "DecidedAtUtc" IS NULL AND "DecidedById" IS NULL)
                OR ("Status" IN (3,4) AND "SubmittedAtUtc" IS NOT NULL
                                      AND "DecidedAtUtc" IS NOT NULL AND "DecidedById" IS NOT NULL)
                """);

            // The real rule, expressed at the point it becomes true -- a Draft survey may be dispatched
            // from a work order that says only "Zayed, Sodic Westown". NOT NULL would have forced a
            // placeholder.
            t.HasCheckConstraint("ck_sites_submitted_needs_address", "\"Status\" = 1 OR \"Address\" IS NOT NULL");
        });

        builder.HasKey(x => x.Id);
        builder.ConfigureEntityConventions();

        // Optimistic concurrency: two surveyors editing one survey from two tablets is the collision
        // this exists for. Mapped to Postgres' system xmin column, so it costs no schema.
        builder.Property<uint>("xmin").IsRowVersion();

        builder.Property(x => x.Code).HasMaxLength(32).IsRequired();
        builder.Property(x => x.Status).HasConversion<int>();

        builder.Property(x => x.Address).HasMaxLength(500);
        builder.Property(x => x.UnitType).HasConversion<int?>();
        builder.Property(x => x.UnitCondition).HasConversion<int?>();
        builder.Property(x => x.BuildingKind).HasConversion<int?>();
        builder.Property(x => x.Position).HasConversion<int?>();
        builder.Property(x => x.ElevatorStatus).HasConversion<int?>();
        builder.Property(x => x.AreaSqm).HasColumnType("numeric(8,2)");
        builder.Property(x => x.UtilityMetersNote).HasColumnType("text");
        builder.Property(x => x.SurveyedOn).HasColumnType("date");
        builder.Property(x => x.Notes).HasColumnType("text");
        builder.Property(x => x.DecisionNote).HasMaxLength(1000);

        // The site list is "this project's surveys, optionally filtered by status", ordered by creation.
        // IncludeProperties makes it an index-only scan: the covering columns are exactly
        // SiteListItemResponse's projection, so adding a field to that DTO means revisiting this index.
        builder.HasIndex(x => new { x.ProjectId, x.Status })
            .HasDatabaseName("ix_sites_project_status")
            .IncludeProperties(x => new { x.Code, x.Address, x.SurveyedOn });

        // ProjectId/CreatedById/DecidedById carry real cross-schema FKs, added by raw SQL at S1-05
        // (projects and hr both precede sites in SchemaOrder). Indexed here so the constraint checks and
        // the "surveys I created" reads are not sequential scans.
        builder.HasIndex(x => x.CreatedById).HasDatabaseName("ix_sites_created_by");

        builder.HasIndex(x => x.DecidedById)
            .HasDatabaseName("ix_sites_decided_by")
            .HasFilter("\"DecidedById\" IS NOT NULL");

        // SourceBriefId gets NO foreign key -- provenance, not a dependency, and nothing in this system
        // writes it (see Site.SourceBriefId). The unique index still stands: if a backfill ever does
        // populate it, one brief must not seed two surveys.
        builder.HasIndex(x => x.SourceBriefId)
            .HasDatabaseName("ux_sites_source_brief")
            .IsUnique()
            .HasFilter("\"SourceBriefId\" IS NOT NULL");

        // ux_sites_code (lower("Code")) and ck_sites_address_blank are raw SQL in the initial migration:
        // EF cannot express a functional index, and both are covered by the R3 schema-object assertion.
    }
}
