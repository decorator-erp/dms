using BuildingBlocks;
using Crm.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Crm.Infrastructure.Persistence.Configurations;

internal sealed class CrmLeadIntakeConfiguration : IEntityTypeConfiguration<CrmLeadIntake>
{
    public void Configure(EntityTypeBuilder<CrmLeadIntake> builder)
    {
        builder.ToTable("crm_lead_intakes");
        builder.ConfigureEntityConventions();

        builder.Property(i => i.JobTitle).HasMaxLength(128);
        builder.Property(i => i.ClientAddress).HasMaxLength(512);
        builder.Property(i => i.Sector).HasConversion<int?>();
        builder.Property(i => i.UnitAddress).HasMaxLength(512);
        builder.Property(i => i.UsedDesignerBefore);
        builder.Property(i => i.DesignerExperience).HasConversion<int?>();
        builder.Property(i => i.DesignerExperienceReason).HasMaxLength(1000);

        // 1:1 with the lead, same schema, a real EF-managed FK.
        builder.HasOne<CrmLead>().WithMany().HasForeignKey(i => i.LeadId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(i => i.LeadId).IsUnique();

        // Residential is configured from ITS OWN side (CrmLeadIntakeResidentialConfiguration) as the
        // dependent naming LeadIntakeId - same parent-configures-nothing / child-owns-the-FK split
        // ProjectDocument/DocumentFolder already establish in Documents. No inverse-navigation FK here.
        builder.Ignore(i => i.Residential);
    }
}
