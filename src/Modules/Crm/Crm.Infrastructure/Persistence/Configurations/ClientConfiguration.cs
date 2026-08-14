using BuildingBlocks;
using Crm.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Crm.Infrastructure.Persistence.Configurations;

internal sealed class ClientConfiguration : IEntityTypeConfiguration<Client>
{
    public void Configure(EntityTypeBuilder<Client> builder)
    {
        builder.ToTable("clients");
        builder.ConfigureEntityConventions();

        // AppUserId: bare string FK to identity.users(Id), no navigation - two different EF models.
        // 450 matches ASP.NET Identity's default Id length, same convention every other module's
        // AppUserId column uses. Raw-SQL constraint added at G9-08.
        builder.Property(c => c.AppUserId).HasMaxLength(450).IsRequired();
        builder.HasIndex(c => c.AppUserId);

        builder.Property(c => c.Address).HasMaxLength(512).IsRequired();
        builder.Property(c => c.Notes).HasMaxLength(2000);
        builder.Property(c => c.JobTitle).HasMaxLength(128);
        builder.Property(c => c.MaritalStatus).HasMaxLength(64);
        builder.Property(c => c.RepresentativeName).HasMaxLength(128);

        // Children persisted as JSONB inside the clients row - no separate table, no joins on read,
        // updated atomically with the client. Ported verbatim from the backend's own ClientConfiguration.
        builder.OwnsMany(c => c.Children, child =>
        {
            child.ToJson("children");
            child.Property(ch => ch.Id);
            child.Property(ch => ch.Gender).HasColumnType("char(1)");
            child.Property(ch => ch.Age);
        });
    }
}
