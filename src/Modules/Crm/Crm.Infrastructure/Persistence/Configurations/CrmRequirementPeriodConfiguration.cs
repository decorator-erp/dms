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

        // ProjectId/ProjectStageId/OpenedById: bare Guid FKs to projects./hr. - raw-SQL constraints at
        // G9-08 (ProjectStageId only where set).
        builder.HasIndex(p => p.ProjectId);
        builder.HasIndex(p => p.ProjectStageId).HasFilter("\"ProjectStageId\" IS NOT NULL");

        // At most one Open period per project - partial unique index, mirroring the backend's
        // ux_crm_requirement_periods_open_per_project.
        builder.HasIndex(p => p.ProjectId)
            .HasDatabaseName("ux_crm_requirement_periods_open_per_project")
            .IsUnique()
            .HasFilter("\"Status\" = 1");
    }
}
