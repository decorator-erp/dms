using BuildingBlocks;
using Finance.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Finance.Infrastructure.Persistence.Configurations;

/// <summary>
/// Ported from <c>../backend/Infrastructure/Persistence/Configurations/LedgerAllocationConfiguration.cs</c>.
///
/// <para><b>The one asymmetric delete rule in the module, and it is deliberate.</b> An allocation
/// cascades from its ledger entry but is restricted by its invoice. An allocation has no meaning apart
/// from the payment it splits, so deleting the payment takes its splits with it; an invoice with money
/// allocated against it must not be deletable at all, because the allocation is the record of that
/// money having been received.</para>
///
/// <para>Both columns are indexed: the bounding invariant sums allocations by entry
/// (<c>LedgerAllocation.WouldExceedUnallocated</c>) and the invoice balance sums them by invoice.</para>
/// </summary>
internal sealed class LedgerAllocationConfiguration : IEntityTypeConfiguration<LedgerAllocation>
{
    public void Configure(EntityTypeBuilder<LedgerAllocation> builder)
    {
        builder.ToTable("ledger_allocations");

        builder.HasKey(x => x.Id);
        builder.ConfigureEntityConventions();

        builder.Property(x => x.Amount).HasColumnType("numeric(19,4)");

        builder.HasOne<LedgerEntry>()
            .WithMany()
            .HasForeignKey(x => x.LedgerEntryId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<Invoice>()
            .WithMany()
            .HasForeignKey(x => x.InvoiceId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(x => x.LedgerEntryId).HasDatabaseName("ix_ledger_allocations_ledger_entry_id");
        builder.HasIndex(x => x.InvoiceId).HasDatabaseName("ix_ledger_allocations_invoice_id");
    }
}
