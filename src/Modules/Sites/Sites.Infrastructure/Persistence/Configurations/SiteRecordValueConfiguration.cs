using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sites.Domain;
using Sites.Domain.Definition;

namespace Sites.Infrastructure.Persistence.Configurations;

/// <summary>
/// The one entity here that is not a <c>SharedKernel.Entity</c>: composite key, no surrogate id, no
/// <c>IsActive</c>, no audit columns, and deliberately <b>no concurrency token</b> — the write path is
/// delete-all-then-reinsert for a whole record, so there is no row to detect a concurrent edit on. The
/// record's own <c>xmin</c> is what guards that.
/// </summary>
internal sealed class SiteRecordValueConfiguration : IEntityTypeConfiguration<SiteRecordValue>
{
    public void Configure(EntityTypeBuilder<SiteRecordValue> builder)
    {
        builder.ToTable("site_record_values", t =>
        {
            // Names the constraint ck_srv_one_value rather than the ck_site_record_values_* convention
            // the rest of this module follows -- deliberate, and preserved from the backend: the schema
            // guard greps for this literal string. num_nonnulls is the database twin of
            // SiteRecordValue.Create's populated-column count.
            t.HasCheckConstraint(
                "ck_srv_one_value",
                "num_nonnulls(\"ValueNumber\", \"ValueText\", \"ValueBool\", \"ValueDate\", \"OptionId\") = 1");
        });

        builder.HasKey(x => new { x.RecordId, x.ItemId })
            .HasName("pk_site_record_values");

        builder.Property(x => x.ValueNumber).HasColumnType("numeric(18,4)");
        builder.Property(x => x.ValueText).HasMaxLength(1024);
        builder.Property(x => x.ValueDate).HasColumnType("date");

        builder.HasOne<SiteRecord>()
            .WithMany()
            .HasForeignKey(x => x.RecordId)
            .OnDelete(DeleteBehavior.Cascade);

        // RESTRICT is what makes an item's code/type/unit/tolerance genuinely immutable-once-used: the
        // ItemLocked check in the handler is the friendly refusal, this is the one that cannot be
        // bypassed.
        builder.HasOne<SiteItem>()
            .WithMany()
            .HasForeignKey(x => x.ItemId)
            .OnDelete(DeleteBehavior.Restrict);

        // Column order is deliberate: the composite option FK is (ItemId, OptionId), so an index whose
        // leading column is ItemId can serve it as an exact prefix match.
        builder.HasIndex(x => new { x.ItemId, x.OptionId })
            .HasDatabaseName("ix_site_record_values_facet");

        // OptionId gets NO single-column FK. The real constraint is composite --
        // (ItemId, OptionId) -> site_item_options (ItemId, Id) -- which is what actually enforces "the
        // chosen option belongs to THIS item". EF cannot model a composite FK to a non-primary unique
        // key, so both it and its target unique index are raw SQL in the initial migration.
    }
}
