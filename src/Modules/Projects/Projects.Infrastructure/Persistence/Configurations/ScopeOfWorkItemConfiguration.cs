using BuildingBlocks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Projects.Domain;

namespace Projects.Infrastructure.Persistence.Configurations;

internal sealed class ScopeOfWorkItemConfiguration : IEntityTypeConfiguration<ScopeOfWorkItem>
{
    public const string SourceStageIndex = "IX_scope_of_work_items_ScopeOfWorkId_SourceStageId";

    public void Configure(EntityTypeBuilder<ScopeOfWorkItem> builder)
    {
        builder.ToTable("scope_of_work_items");
        builder.ConfigureEntityConventions();

        builder.Property(i => i.Name).HasMaxLength(256).IsRequired();
        builder.Property(i => i.Description).HasMaxLength(2000);
        builder.Property(i => i.Fee).HasPrecision(18, 2);

        // SourceStageId is a plain id with no foreign key: the template stage may be deleted later, and
        // confirm refuses that case (SourceStageNotFound). Picking it twice trips this index.
        builder.HasIndex(i => new { i.ScopeOfWorkId, i.SourceStageId }).IsUnique().HasDatabaseName(SourceStageIndex);

        // Not unique: a reorder rewrites every order in one save, so a unique index would trip on the
        // intermediate states. Contiguous 1..n is the aggregate's rule.
        builder.HasIndex(i => new { i.ScopeOfWorkId, i.Order });
        builder.HasIndex(i => i.ScopeOfWorkId);
    }
}
