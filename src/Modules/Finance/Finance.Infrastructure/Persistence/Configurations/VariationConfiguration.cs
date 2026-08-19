using BuildingBlocks;
using Finance.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Finance.Infrastructure.Persistence.Configurations;

/// <summary>
/// Ported from <c>../backend/Infrastructure/Persistence/Configurations/VariationConfiguration.cs</c>.
///
/// <para><b>The two check constraints are the database half of the kind discriminator</b>, and they are
/// why a variation cannot be half of one kind and half of the other. <c>Variation.Create</c> enforces
/// the same two rules in the domain; these enforce them against every path that reaches the table,
/// including a future bulk update or a hand-run correction.</para>
///
/// <para><b>They are rewritten, not copied.</b> The backend stores <c>Kind</c> as a string, so its
/// constraints read <c>kind &lt;&gt; 'ClientDifference'</c>. This module stores enums as <c>int</c> like
/// every other module here, so the comparisons are against the numeric values —
/// <c>ClientDifference = 1</c>, <c>InternalChange = 2</c> — and the column names are quoted PascalCase,
/// because this repo does not map columns to snake_case and an unquoted identifier would be folded to
/// lowercase by Postgres and silently reference a column that does not exist.
///
/// <b>A renumbered <c>VariationKind</c> would break both constraints silently</b>, which is one more
/// reason <c>Finance.UnitTests.EnumParityTests</c> pins the numeric values, and why
/// <c>FinanceSchemaObjectTests</c> asserts these two by name and proves one of them actually bites.</para>
/// </summary>
internal sealed class VariationConfiguration : IEntityTypeConfiguration<Variation>
{
    public void Configure(EntityTypeBuilder<Variation> builder)
    {
        builder.ToTable("variations", t =>
        {
            t.HasCheckConstraint(
                "ck_variations_client_diff",
                "\"Kind\" <> 1 OR (\"ProjectTaskId\" IS NOT NULL AND \"BudgetedAmount\" IS NOT NULL "
                + "AND \"ChosenAmount\" IS NOT NULL)");

            t.HasCheckConstraint(
                "ck_variations_internal",
                "\"Kind\" <> 2 OR (\"BudgetLineId\" IS NOT NULL AND \"SignedByClientId\" IS NULL "
                + "AND \"SignedDocumentPath\" IS NULL)");
        });

        builder.HasKey(x => x.Id);
        builder.ConfigureEntityConventions();

        builder.Property(x => x.Kind).HasConversion<int>();
        builder.Property(x => x.Status).HasConversion<int>();

        builder.Property(x => x.Reason).HasMaxLength(256).IsRequired();
        builder.Property(x => x.Description).HasMaxLength(512);
        builder.Property(x => x.SignedDocumentPath).HasMaxLength(512);
        builder.Property(x => x.ResolutionNote).HasMaxLength(512);

        builder.Property(x => x.Amount).HasColumnType("numeric(19,4)");
        builder.Property(x => x.BudgetedAmount).HasColumnType("numeric(19,4)");
        builder.Property(x => x.ChosenAmount).HasColumnType("numeric(19,4)");

        // Derived from SignedAt. The signature is a timestamp, not a flag, so there is one thing to get
        // wrong rather than two that can disagree.
        builder.Ignore(x => x.IsSigned);

        builder.HasOne<BudgetLine>()
            .WithMany()
            .HasForeignKey(x => x.BudgetLineId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(x => new { x.ProjectId, x.Status })
            .HasDatabaseName("ix_variations_project_id_status");

        builder.HasIndex(x => x.BudgetLineId).HasDatabaseName("ix_variations_budget_line_id");
        builder.HasIndex(x => x.ProjectTaskId).HasDatabaseName("ix_variations_project_task_id");
        builder.HasIndex(x => x.ProjectStageId).HasDatabaseName("ix_variations_project_stage_id");
        builder.HasIndex(x => x.Kind).HasDatabaseName("ix_variations_kind");

        // Explicit because the backend got this one free from HasOne(Client), and F1-05 needs every
        // cross-schema foreign key column indexed.
        builder.HasIndex(x => x.SignedByClientId).HasDatabaseName("ix_variations_signed_by_client_id");
        builder.HasIndex(x => x.RequestedById).HasDatabaseName("ix_variations_requested_by_id");
        builder.HasIndex(x => x.ApprovedById).HasDatabaseName("ix_variations_approved_by_id");
    }
}
