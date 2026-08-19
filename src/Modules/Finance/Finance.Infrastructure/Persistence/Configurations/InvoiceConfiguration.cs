using BuildingBlocks;
using Finance.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Finance.Infrastructure.Persistence.Configurations;

/// <summary>
/// Ported from <c>../backend/Infrastructure/Persistence/Configurations/InvoiceConfiguration.cs</c>.
///
/// <para><b><see cref="Invoice.Number"/> is globally unique, not per-project.</b> Ported verbatim
/// because that is what the backend enforces and what its duplicate check assumes — and it is the
/// reason <c>Invoice.Create</c> now rejects a blank number (D-F1, fixed at F1-03): a unique index
/// permits exactly one blank, so without that guard the first empty-numbered invoice would succeed and
/// the second would surface as a database error rather than a validation failure.</para>
/// </summary>
internal sealed class InvoiceConfiguration : IEntityTypeConfiguration<Invoice>
{
    public void Configure(EntityTypeBuilder<Invoice> builder)
    {
        builder.ToTable("invoices");

        builder.HasKey(x => x.Id);
        builder.ConfigureEntityConventions();

        builder.Property(x => x.Number).HasMaxLength(48).IsRequired();
        builder.Property(x => x.VendorName).HasMaxLength(128);
        builder.Property(x => x.Note).HasMaxLength(512);

        builder.Property(x => x.Type).HasConversion<int>();
        builder.Property(x => x.Status).HasConversion<int>();

        builder.Property(x => x.IssuedOn).HasColumnType("date");
        builder.Property(x => x.DueOn).HasColumnType("date");

        builder.Property(x => x.TotalAmount).HasColumnType("numeric(19,4)").HasDefaultValue(0m);
        builder.Property(x => x.PaidAmount).HasColumnType("numeric(19,4)").HasDefaultValue(0m);

        // Both derived from the two money columns above. Storing Balance would let it disagree with
        // TotalAmount - PaidAmount, which is the disagreement ApplyPayment exists to prevent.
        builder.Ignore(x => x.Balance);
        builder.Ignore(x => x.IsPayable);

        builder.HasIndex(x => x.Number)
            .IsUnique()
            .HasDatabaseName("ix_invoices_number");

        builder.HasIndex(x => new { x.ProjectId, x.Status })
            .HasDatabaseName("ix_invoices_project_id_status");

        builder.HasIndex(x => x.ProjectStageId).HasDatabaseName("ix_invoices_project_stage_id");
        builder.HasIndex(x => x.ClientId).HasDatabaseName("ix_invoices_client_id");

        // F1-08 reads this table by due date to find overdue and due-soon invoices; without it the job
        // sequentially scans every invoice ever issued on every tick.
        builder.HasIndex(x => x.DueOn).HasDatabaseName("ix_invoices_due_on");
    }
}
