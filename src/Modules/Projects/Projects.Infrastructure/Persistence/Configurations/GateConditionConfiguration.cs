using BuildingBlocks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Projects.Domain;

namespace Projects.Infrastructure.Persistence.Configurations;

internal sealed class GateConditionConfiguration : IEntityTypeConfiguration<GateCondition>
{
    public void Configure(EntityTypeBuilder<GateCondition> builder)
    {
        builder.ToTable("gate_conditions");
        builder.ConfigureEntityConventions();

        builder.Property(g => g.ConditionType).HasConversion<int>();
        builder.Property(g => g.Trigger).HasConversion<int>();
        builder.Property(g => g.Label).HasMaxLength(200).IsRequired();
        builder.Property(g => g.ApprovalType).HasConversion<int?>();
        builder.Property(g => g.Status).HasConversion<int>();
        builder.Property(g => g.ResolutionNote).HasMaxLength(2000);

        builder.Ignore(g => g.IsSatisfied);
        builder.Ignore(g => g.IsRejected);
        builder.Ignore(g => g.IsPending);
        builder.Ignore(g => g.Scope);

        builder.HasIndex(g => g.ProjectStageId);
        builder.HasIndex(g => g.ProjectTaskId);
        // ResolvedById: bare Guid FK to hr.employees, raw-SQL constraint in the migration.
        // ApprovalId: bare Guid FK to approvals.approvals (nullable), raw-SQL constraint in the migration.
        builder.HasIndex(g => g.ApprovalId);
    }
}
