using BuildingBlocks;
using Crm.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Crm.Infrastructure.Persistence.Configurations;

internal sealed class CrmClientNoteConfiguration : IEntityTypeConfiguration<CrmClientNote>
{
    public void Configure(EntityTypeBuilder<CrmClientNote> builder)
    {
        builder.ToTable("crm_client_notes");
        builder.ConfigureEntityConventions();

        builder.Property(n => n.Content).HasMaxLength(4000).IsRequired();

        builder.HasOne<Client>().WithMany().HasForeignKey(n => n.ClientId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(n => n.ClientId);

        // ProjectId: bare Guid, deliberately NO FK - see CrmMeetingConfiguration's note (crm precedes
        // projects in SchemaOrder; crm -> projects is not in rule 9's allow-list). Nullable regardless.
        // WrittenById: bare Guid FK to hr.employees(Id) - raw-SQL constraint added at G9-08.
        builder.HasIndex(n => n.ProjectId).HasFilter("\"ProjectId\" IS NOT NULL");
        builder.HasIndex(n => n.WrittenById);
    }
}
