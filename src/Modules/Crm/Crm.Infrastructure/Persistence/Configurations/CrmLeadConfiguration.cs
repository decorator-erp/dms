using BuildingBlocks;
using Crm.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Crm.Infrastructure.Persistence.Configurations;

internal sealed class CrmLeadConfiguration : IEntityTypeConfiguration<CrmLead>
{
    public void Configure(EntityTypeBuilder<CrmLead> builder)
    {
        builder.ToTable("crm_leads");
        builder.ConfigureEntityConventions();

        builder.Property(l => l.FirstName).HasMaxLength(128).IsRequired();
        builder.Property(l => l.LastName).HasMaxLength(128).IsRequired();
        builder.Property(l => l.Email).HasMaxLength(256);
        builder.Property(l => l.Phone).HasMaxLength(32);
        builder.Property(l => l.Source).HasConversion<int>();
        builder.Property(l => l.Status).HasConversion<int>();
        builder.Property(l => l.Notes).HasMaxLength(2000);

        // OwnerId: bare Guid FK to hr.employees(Id), no navigation - raw-SQL constraint at G9-08.
        builder.HasIndex(l => l.OwnerId);

        // ConvertedClientId: same-schema, a real EF-managed FK, nullable (set only after conversion).
        builder.HasOne<Client>().WithMany().HasForeignKey(l => l.ConvertedClientId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(l => l.ConvertedClientId).HasFilter("\"ConvertedClientId\" IS NOT NULL");

        builder.HasIndex(l => l.Status);
    }
}
