using BuildingBlocks;
using Crm.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Crm.Infrastructure.Persistence.Configurations;

internal sealed class CrmLeadIntakeResidentialConfiguration : IEntityTypeConfiguration<CrmLeadIntakeResidential>
{
    public void Configure(EntityTypeBuilder<CrmLeadIntakeResidential> builder)
    {
        builder.ToTable("crm_lead_intake_residentials");
        builder.Property(r => r.Id).ValueGeneratedNever();
        // Not IAuditable (no CreatedAtUtc/UpdatedAtUtc/IsActive column) - matches the entity's own base
        // type (Entity only, no IAuditable), same as the backend.

        builder.Property(r => r.UnitType).HasConversion<int?>();
        builder.Property(r => r.UnitCondition).HasConversion<int?>();
        builder.Property(r => r.FinishingPurpose).HasConversion<int?>();

        builder.HasOne<CrmLeadIntake>().WithOne().HasForeignKey<CrmLeadIntakeResidential>(r => r.LeadIntakeId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(r => r.LeadIntakeId).IsUnique();
    }
}
