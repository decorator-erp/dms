using BuildingBlocks;
using Crm.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Crm.Infrastructure.Persistence.Configurations;

internal sealed class CrmSpaceConfiguration : IEntityTypeConfiguration<CrmSpace>
{
    public void Configure(EntityTypeBuilder<CrmSpace> builder)
    {
        builder.ToTable("crm_spaces");
        builder.ConfigureEntityConventions();

        builder.Property(s => s.Name).HasMaxLength(256).IsRequired();
        builder.Property(s => s.Description).HasMaxLength(1000);

        // ProjectId: bare Guid, deliberately NO FK - see CrmMeetingConfiguration's note (crm precedes
        // projects in SchemaOrder; crm -> projects is not in rule 9's allow-list).
        builder.HasIndex(s => s.ProjectId);

        builder.HasOne<CrmSpaceType>().WithMany().HasForeignKey(s => s.SpaceTypeId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(s => s.SpaceTypeId);

        // Unique per project, case-insensitively - mirroring the backend's ux_crm_spaces_project_name.
        builder.HasIndex(s => new { s.ProjectId, s.Name })
            .HasDatabaseName("ux_crm_spaces_project_name")
            .IsUnique();
    }
}
