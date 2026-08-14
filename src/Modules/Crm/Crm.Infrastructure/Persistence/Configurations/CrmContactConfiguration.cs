using BuildingBlocks;
using Crm.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Crm.Infrastructure.Persistence.Configurations;

internal sealed class CrmContactConfiguration : IEntityTypeConfiguration<CrmContact>
{
    public void Configure(EntityTypeBuilder<CrmContact> builder)
    {
        builder.ToTable("crm_contacts");
        builder.ConfigureEntityConventions();

        builder.Property(c => c.Role).HasConversion<int>();
        builder.Property(c => c.FirstName).HasMaxLength(128).IsRequired();
        builder.Property(c => c.LastName).HasMaxLength(128).IsRequired();
        builder.Property(c => c.Email).HasMaxLength(256);
        builder.Property(c => c.Phone).HasMaxLength(32);
        builder.Property(c => c.Mobile).HasMaxLength(32);
        builder.Property(c => c.Notes).HasMaxLength(2000);

        // ClientId: same-schema, a real EF-managed FK.
        builder.HasOne<Client>().WithMany().HasForeignKey(c => c.ClientId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(c => c.ClientId);

        // At most one primary contact per client - partial unique index, mirroring the backend.
        builder.HasIndex(c => c.ClientId)
            .HasDatabaseName("ux_crm_contacts_client_primary")
            .IsUnique()
            .HasFilter("\"IsPrimary\" = true");
    }
}
