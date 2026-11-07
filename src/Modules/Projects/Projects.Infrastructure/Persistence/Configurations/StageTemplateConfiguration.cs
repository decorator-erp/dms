using BuildingBlocks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Projects.Domain;

namespace Projects.Infrastructure.Persistence.Configurations;

internal sealed class StageTemplateConfiguration : IEntityTypeConfiguration<StageTemplate>
{
    /// <summary>The unique name index, which <c>StageTemplateWrites</c> maps to a 409.</summary>
    public const string NameIndex = "IX_stage_templates_Name";

    public void Configure(EntityTypeBuilder<StageTemplate> builder)
    {
        builder.ToTable("stage_templates");
        builder.ConfigureEntityConventions();

        builder.Property(t => t.Name).HasMaxLength(200).IsRequired();
        builder.Property(t => t.Description).HasMaxLength(2000);
        builder.Property(t => t.Type).HasConversion<int>();

        builder.HasIndex(t => t.Name).IsUnique().HasDatabaseName(NameIndex);
    }
}
