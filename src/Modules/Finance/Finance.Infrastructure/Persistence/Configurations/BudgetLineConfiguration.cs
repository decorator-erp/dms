using BuildingBlocks;
using Finance.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Finance.Infrastructure.Persistence.Configurations;

/// <summary>
/// Ported from <c>../backend/Infrastructure/Persistence/Configurations/BudgetLineConfiguration.cs</c>.
///
/// <para><b>Four money columns, all <c>numeric(19,4)</c> defaulting to zero.</b> The defaults are
/// load-bearing rather than cosmetic: <c>ApplyCommitment</c> and <c>ApplyActualCost</c> move these by a
/// delta, so a null would poison every subsequent arithmetic operation on the row.</para>
///
/// <para><b>ProjectId gets an explicit index the backend never had to write.</b> There, the index came
/// free from <c>HasOne(x => x.Project)</c>; here the navigation is gone (arch rule 3 forbids navigating
/// to another module's entity type) and with it the implicit index. Every read in this module filters
/// by project, and F1-05 requires each cross-schema foreign key column to be indexed, so the index has
/// to be declared rather than inherited.</para>
/// </summary>
internal sealed class BudgetLineConfiguration : IEntityTypeConfiguration<BudgetLine>
{
    public void Configure(EntityTypeBuilder<BudgetLine> builder)
    {
        builder.ToTable("budget_lines");

        builder.HasKey(x => x.Id);
        builder.ConfigureEntityConventions();

        builder.Property(x => x.Name).HasMaxLength(128).IsRequired();
        builder.Property(x => x.Code).HasMaxLength(32);
        builder.Property(x => x.Description).HasMaxLength(512);

        builder.Property(x => x.AllocatedAmount).HasColumnType("numeric(19,4)").HasDefaultValue(0m);
        builder.Property(x => x.AdjustmentAllocation).HasColumnType("numeric(19,4)").HasDefaultValue(0m);
        builder.Property(x => x.CommittedAmount).HasColumnType("numeric(19,4)").HasDefaultValue(0m);
        builder.Property(x => x.ActualAmount).HasColumnType("numeric(19,4)").HasDefaultValue(0m);

        // Derived on the entity, never stored. EffectiveAllocation is the ceiling the Budget gate reads
        // and AvailableAmount is what InsufficientBudget is measured against; persisting either would
        // let the stored value drift from the four columns it summarises.
        builder.Ignore(x => x.EffectiveAllocation);
        builder.Ignore(x => x.AvailableAmount);

        // The one intra-schema relationship on this table. SetNull, not Restrict: a cost category is
        // reference data, and losing the classification must not block the delete or orphan the budget.
        builder.HasOne<CostCategory>()
            .WithMany()
            .HasForeignKey(x => x.CostCategoryId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasIndex(x => x.ProjectId).HasDatabaseName("ix_budget_lines_project_id");
        builder.HasIndex(x => x.CostCategoryId).HasDatabaseName("ix_budget_lines_cost_category_id");
        builder.HasIndex(x => x.ProjectStageId).HasDatabaseName("ix_budget_lines_project_stage_id");
        builder.HasIndex(x => x.ProjectTaskId).HasDatabaseName("ix_budget_lines_project_task_id");
    }
}
