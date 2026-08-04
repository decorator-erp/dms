using BuildingBlocks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Projects.Domain;

namespace Projects.Infrastructure.Persistence.Configurations;

internal sealed class StageSubTaskConfiguration : IEntityTypeConfiguration<StageSubTask>
{
    public void Configure(EntityTypeBuilder<StageSubTask> builder)
    {
        builder.ToTable("stage_sub_tasks");
        builder.ConfigureEntityConventions();

        builder.Property(s => s.Title).HasMaxLength(200).IsRequired();
        builder.Property(s => s.Description).HasMaxLength(2000);

        builder.HasIndex(s => s.StageTaskId);
    }
}
