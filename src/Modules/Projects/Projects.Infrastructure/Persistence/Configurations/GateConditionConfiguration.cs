using BuildingBlocks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Projects.Domain;

namespace Projects.Infrastructure.Persistence.Configurations;

internal sealed class GateConditionConfiguration : IEntityTypeConfiguration<GateCondition>
{
    public void Configure(EntityTypeBuilder<GateCondition> builder)
    {
        // NP4: each enum column refuses a value outside its enum (a NULL passes a CHECK).
        builder.ToTable("gate_conditions", t =>
        {
            t.HasCheckConstraint("CK_gate_conditions_ConditionType", "\"ConditionType\" BETWEEN 1 AND 10");
            t.HasCheckConstraint("CK_gate_conditions_Trigger", "\"Trigger\" BETWEEN 1 AND 2");
            t.HasCheckConstraint("CK_gate_conditions_ApprovalType", "\"ApprovalType\" BETWEEN 1 AND 2");
            t.HasCheckConstraint("CK_gate_conditions_Status", "\"Status\" BETWEEN 1 AND 3");
        });
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
        // F1-00: VariationId is a bare Guid pointing at finance.variations with no FK (see the entity's
        // own note and the migration). Indexed because the satisfaction path looks a gate up BY variation
        // id when a variation is approved — the backend indexes it for the same reason.
        builder.HasIndex(g => g.VariationId);
    }
}
