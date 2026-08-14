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

        // ProjectId: bare Guid FK to projects.projects(Id) - raw-SQL constraint at G9-08, nullable.
        // WrittenById: bare Guid FK to hr.employees(Id) - raw-SQL at G9-08.
        builder.HasIndex(n => n.ProjectId).HasFilter("\"ProjectId\" IS NOT NULL");
    }
}
