using Finance.Domain;
using SharedKernel;
using Xunit;

namespace Finance.UnitTests;

public class InvoiceTests
{
    private static readonly Guid ProjectId = Guid.NewGuid();
    private static readonly Guid ClientId = Guid.NewGuid();
    private static readonly DateOnly Issued = new(2026, 8, 1);

    private static Invoice ClientInvoice() =>
        Invoice.Create("INV-1", ProjectId, InvoiceType.ClientInvoice, Issued, clientId: ClientId).Value!;

    // ── Creation ───────────────────────────────────────────────────────────────

    /// <summary>DIVERGENCE from the backend, which never checks the number in <c>Create</c> and relies
    /// on an edge validator — so a handler constructing an invoice directly could persist one with an
    /// empty natural key, which is also what the duplicate check keys on.</summary>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_WithoutANumber_FailsWithNumberRequired(string number)
    {
        Result<Invoice> result = Invoice.Create(number, ProjectId, InvoiceType.ClientInvoice, Issued, clientId: ClientId);

        Assert.True(result.IsFailure);
        Assert.Equal(InvoiceErrors.NumberRequired, result.Error);
    }

    [Fact]
    public void ClientInvoice_RequiresAClient()
    {
        Result<Invoice> result = Invoice.Create("INV-1", ProjectId, InvoiceType.ClientInvoice, Issued);

        Assert.True(result.IsFailure);
        Assert.Equal(InvoiceErrors.ClientRequired, result.Error);
    }

    [Fact]
    public void VendorInvoice_RequiresAVendorName()
    {
        foreach (string? vendor in new[] { null, "", "   " })
        {
            Result<Invoice> result = Invoice.Create("INV-1", ProjectId, InvoiceType.VendorInvoice, Issued, vendorName: vendor);

            Assert.True(result.IsFailure);
            Assert.Equal(InvoiceErrors.VendorRequired, result.Error);
        }

        Assert.True(Invoice.Create("INV-1", ProjectId, InvoiceType.VendorInvoice, Issued, vendorName: "Acme").IsSuccess);
    }

    [Fact]
    public void Create_StartsOpenAndUnpaid()
    {
        Invoice invoice = ClientInvoice();

        Assert.Equal(InvoiceStatus.Open, invoice.Status);
        Assert.Equal(0m, invoice.TotalAmount);
        Assert.Equal(0m, invoice.PaidAmount);
        Assert.Equal(0m, invoice.Balance);
        Assert.True(invoice.IsPayable);
    }

    // ── Payment ────────────────────────────────────────────────────────────────

    [Fact]
    public void ApplyPayment_ReducesTheBalance_AndFlipsToPaidWhenItReachesZero()
    {
        Invoice invoice = ClientInvoice();
        invoice.SetTotal(1000m);

        Assert.True(invoice.ApplyPayment(400m).IsSuccess);
        Assert.Equal(600m, invoice.Balance);
        Assert.Equal(InvoiceStatus.Open, invoice.Status);

        Assert.True(invoice.ApplyPayment(600m).IsSuccess);
        Assert.Equal(0m, invoice.Balance);
        Assert.Equal(InvoiceStatus.Paid, invoice.Status);
        Assert.False(invoice.IsPayable);
    }

    /// <summary>The invoice-side bound. A payment may settle the balance exactly but never exceed it —
    /// the complement of <c>LedgerAllocation.WouldExceedUnallocated</c>, which bounds the same money
    /// from the payment's side. Both are needed: one stops over-paying an invoice, the other stops
    /// over-spending a payment.</summary>
    [Theory]
    [InlineData(1000.0001)]
    [InlineData(2000)]
    [InlineData(0)]
    [InlineData(-1)]
    public void ApplyPayment_OutsideTheBalance_FailsWithOverAllocation(decimal amount)
    {
        Invoice invoice = ClientInvoice();
        invoice.SetTotal(1000m);

        Result result = invoice.ApplyPayment(amount);

        Assert.True(result.IsFailure);
        Assert.Equal(InvoiceErrors.OverAllocation, result.Error);
        Assert.Equal(0m, invoice.PaidAmount);
    }

    [Fact]
    public void ApplyPayment_ToAPaidInvoice_FailsWithNotPayable()
    {
        Invoice invoice = ClientInvoice();
        invoice.SetTotal(100m);
        invoice.ApplyPayment(100m);

        Result result = invoice.ApplyPayment(1m);

        // NotPayable, not OverAllocation: the status guard runs first, and conflating the two would
        // hide a status bug behind an amount assertion.
        Assert.True(result.IsFailure);
        Assert.Equal(InvoiceErrors.NotPayable, result.Error);
    }

    [Fact]
    public void ApplyPayment_ToACancelledInvoice_FailsWithNotPayable()
    {
        Invoice invoice = ClientInvoice();
        invoice.SetTotal(100m);
        invoice.Cancel();

        Assert.Equal(InvoiceErrors.NotPayable, invoice.ApplyPayment(10m).Error);
    }

    // ── Cancel ─────────────────────────────────────────────────────────────────

    [Fact]
    public void Cancel_AnUntouchedInvoice_Succeeds()
    {
        Invoice invoice = ClientInvoice();
        invoice.SetTotal(100m);

        Assert.True(invoice.Cancel().IsSuccess);
        Assert.Equal(InvoiceStatus.Cancelled, invoice.Status);
    }

    /// <summary>A part-paid invoice must have its payments reversed first, so the ledger and the
    /// invoice can never disagree about money already received.</summary>
    [Fact]
    public void Cancel_APartPaidInvoice_FailsWithInvalidStatusTransition()
    {
        Invoice invoice = ClientInvoice();
        invoice.SetTotal(1000m);
        invoice.ApplyPayment(1m);

        Result result = invoice.Cancel();

        Assert.True(result.IsFailure);
        Assert.Equal(InvoiceErrors.InvalidStatusTransition, result.Error);
        Assert.Equal(InvoiceStatus.Open, invoice.Status);
    }

    [Fact]
    public void Cancel_IsNotRepeatable_AndAPaidInvoiceCannotBeCancelled()
    {
        Invoice cancelled = ClientInvoice();
        cancelled.Cancel();
        Assert.Equal(InvoiceErrors.InvalidStatusTransition, cancelled.Cancel().Error);

        Invoice paid = ClientInvoice();
        paid.SetTotal(50m);
        paid.ApplyPayment(50m);
        Assert.Equal(InvoiceErrors.InvalidStatusTransition, paid.Cancel().Error);
    }
}

public class InvoiceLineTests
{
    private static readonly Guid InvoiceId = Guid.NewGuid();

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_WithoutADescription_Fails(string description)
    {
        Result<InvoiceLine> result = InvoiceLine.Create(InvoiceId, description, 1m, 10m);

        Assert.True(result.IsFailure);
        Assert.Equal(InvoiceLineErrors.DescriptionRequired, result.Error);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Create_WithNonPositiveQuantity_Fails(decimal quantity)
    {
        Result<InvoiceLine> result = InvoiceLine.Create(InvoiceId, "Ply sheets", quantity, 10m);

        Assert.True(result.IsFailure);
        Assert.Equal(InvoiceLineErrors.InvalidQuantity, result.Error);
    }

    /// <summary>DIVERGENCE from the backend, which guards quantity but not unit price — a negative
    /// price produces a negative line total and silently reduces the invoice header and the BOQ
    /// purchase total.</summary>
    [Fact]
    public void Create_WithNegativeUnitPrice_FailsWithInvalidUnitPrice()
    {
        Result<InvoiceLine> result = InvoiceLine.Create(InvoiceId, "Ply sheets", 1m, -0.0001m);

        Assert.True(result.IsFailure);
        Assert.Equal(InvoiceLineErrors.InvalidUnitPrice, result.Error);
    }

    /// <summary>Zero stays legal — a no-charge line is a real thing.</summary>
    [Fact]
    public void Create_WithZeroUnitPrice_Succeeds()
    {
        Result<InvoiceLine> result = InvoiceLine.Create(InvoiceId, "Free sample", 2m, 0m);

        Assert.True(result.IsSuccess);
        Assert.Equal(0m, result.Value!.LineTotal);
    }

    [Fact]
    public void LineTotal_IsQuantityTimesUnitPrice()
    {
        InvoiceLine line = InvoiceLine.Create(InvoiceId, "Ply sheets", 3m, 12.5m).Value!;

        Assert.Equal(37.5m, line.LineTotal);
    }

    /// <summary>
    /// The one rounding rule in the whole module, pinned: scale 4 with .NET's default
    /// <c>MidpointRounding.ToEven</c> (banker's). <c>MidpointRounding</c> appears nowhere in the
    /// backend, so the default IS the rule.
    ///
    /// <para>Both midpoints are asserted because they round in opposite directions —
    /// <c>0.00005</c> → <c>0.0000</c> and <c>0.00015</c> → <c>0.0002</c>, each to the even digit.
    /// Switching to <c>AwayFromZero</c> would give <c>0.0001</c> and <c>0.0002</c>, so only the first
    /// case distinguishes the two policies; asserting one alone would not.</para>
    /// </summary>
    [Theory]
    [InlineData(0.00005, 0.0000)]
    [InlineData(0.00015, 0.0002)]
    [InlineData(0.00025, 0.0002)]
    [InlineData(0.00035, 0.0004)]
    public void LineTotal_RoundsToScale4_UsingBankersRounding(decimal unitPrice, decimal expected)
    {
        InvoiceLine line = InvoiceLine.Create(InvoiceId, "Rounding probe", 1m, unitPrice).Value!;

        Assert.Equal(expected, line.LineTotal);
    }

    [Fact]
    public void LineTotal_RoundsAProductThatExceedsScale4()
    {
        // 3 * 1.111111 = 3.333333 -> 3.3333
        InvoiceLine line = InvoiceLine.Create(InvoiceId, "Ply sheets", 3m, 1.111111m).Value!;

        Assert.Equal(3.3333m, line.LineTotal);
    }
}

public class BoqSnapshotLineTests
{
    private static readonly Guid SnapshotId = Guid.NewGuid();

    private static BoqSnapshotLine Line(Guid? invoiceLineId = null, Guid? variationId = null) =>
        BoqSnapshotLine.Create(
            SnapshotId, "line", 100m, 90m, 10m, invoiceLineId: invoiceLineId, variationId: variationId);

    [Fact]
    public void Kind_IsBudgetItem_WhenNeitherSourceIdIsSet() =>
        Assert.Equal(BoqSnapshotLineKind.BudgetItem, Line().Kind);

    [Fact]
    public void Kind_IsPurchase_WhenOnlyTheInvoiceLineIsSet() =>
        Assert.Equal(BoqSnapshotLineKind.Purchase, Line(invoiceLineId: Guid.NewGuid()).Kind);

    [Fact]
    public void Kind_IsDifference_WhenOnlyTheVariationIsSet() =>
        Assert.Equal(BoqSnapshotLineKind.Difference, Line(variationId: Guid.NewGuid()).Kind);

    /// <summary>
    /// The precedence case, and the reason <c>Kind</c> exists at all: a line can carry <b>both</b> ids,
    /// and the classification is ordered, not exclusive. A reader that tested <c>InvoiceLineId</c>
    /// first would call this a Purchase and under-report the difference total.
    /// </summary>
    [Fact]
    public void Kind_PrefersDifference_WhenBothSourceIdsAreSet() =>
        Assert.Equal(
            BoqSnapshotLineKind.Difference,
            Line(invoiceLineId: Guid.NewGuid(), variationId: Guid.NewGuid()).Kind);
}
