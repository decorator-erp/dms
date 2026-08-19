using BuildingBlocks;
using Finance.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Finance.Infrastructure.Persistence.Configurations;

/// <summary>
/// Ported from <c>../backend/Infrastructure/Persistence/Configurations/InvoiceLineConfiguration.cs</c>.
///
/// <para><b><see cref="InvoiceLine.LineTotal"/>'s column scale is the rounding rule's other half.</b>
/// <c>InvoiceLine.Create</c> rounds the product to 4 places precisely because the column is
/// <c>numeric(19,4)</c>; widening one without the other reintroduces the silent truncation the rounding
/// exists to make explicit. This is the only rounding site in the module.</para>
///
/// <para>Lines cascade from their invoice — a line has no meaning without its header, and the header
/// total is recomputed from the surviving lines.</para>
/// </summary>
internal sealed class InvoiceLineConfiguration : IEntityTypeConfiguration<InvoiceLine>
{
    public void Configure(EntityTypeBuilder<InvoiceLine> builder)
    {
        builder.ToTable("invoice_lines");

        builder.HasKey(x => x.Id);
        builder.ConfigureEntityConventions();

        builder.Property(x => x.Description).HasMaxLength(256).IsRequired();
        builder.Property(x => x.Unit).HasMaxLength(16);

        builder.Property(x => x.Quantity).HasColumnType("numeric(19,4)").HasDefaultValue(1m);
        builder.Property(x => x.UnitPrice).HasColumnType("numeric(19,4)");
        builder.Property(x => x.LineTotal).HasColumnType("numeric(19,4)");

        builder.HasOne<Invoice>()
            .WithMany()
            .HasForeignKey(x => x.InvoiceId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<BudgetLine>()
            .WithMany()
            .HasForeignKey(x => x.BudgetLineId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(x => x.InvoiceId).HasDatabaseName("ix_invoice_lines_invoice_id");
        builder.HasIndex(x => x.ProjectTaskId).HasDatabaseName("ix_invoice_lines_project_task_id");
        builder.HasIndex(x => x.BudgetLineId).HasDatabaseName("ix_invoice_lines_budget_line_id");
    }
}
