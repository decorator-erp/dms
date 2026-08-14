using BuildingBlocks;
using Crm.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Crm.Infrastructure.Persistence.Configurations;

internal sealed class CrmRequirementPeriodConfiguration : IEntityTypeConfiguration<CrmRequirementPeriod>
{
    public void Configure(EntityTypeBuilder<CrmRequirementPeriod> builder)
    {
        builder.ToTable("crm_requirement_periods");
        builder.ConfigureEntityConventions();

        builder.Property(p => p.Status).HasConversion<int>();

        // ProjectId/ProjectStageId: bare Guids, deliberately NO FK - see CrmMeetingConfiguration's note
        // (crm precedes projects in SchemaOrder; crm -> projects is not in rule 9's allow-list).
        // OpenedById: bare Guid FK to hr.employees(Id) - raw-SQL constraint added at G9-08.
        builder.HasIndex(p => p.ProjectId);
        builder.HasIndex(p => p.ProjectStageId).HasFilter("\"ProjectStageId\" IS NOT NULL");
        builder.HasIndex(p => p.OpenedById);

        // At most one Open period per project - partial unique index, mirroring the backend's
        // ux_crm_requirement_periods_open_per_project.
        builder.HasIndex(p => p.ProjectId)
            .HasDatabaseName("ux_crm_requirement_periods_open_per_project")
            .IsUnique()
            .HasFilter("\"Status\" = 1");
    }
}
