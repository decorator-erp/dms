using BuildingBlocks;
using Documents.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Documents.Infrastructure.Persistence.Configurations;

internal sealed class FileTemplateConfiguration : IEntityTypeConfiguration<FileTemplate>
{
    public void Configure(EntityTypeBuilder<FileTemplate> builder)
    {
        builder.ToTable("file_templates");
        builder.ConfigureEntityConventions();

        builder.Property(t => t.Name).HasMaxLength(256).IsRequired();
        builder.Property(t => t.Description);
        builder.Property(t => t.Category).HasConversion<int>();
        builder.Property(t => t.FileName).HasMaxLength(512).IsRequired();
        builder.Property(t => t.StoragePath).HasMaxLength(1024).IsRequired();
        builder.Property(t => t.ContentType).HasMaxLength(256).IsRequired();

        builder.HasIndex(t => t.Name).IsUnique();
        builder.HasIndex(t => t.Category);
    }
}
