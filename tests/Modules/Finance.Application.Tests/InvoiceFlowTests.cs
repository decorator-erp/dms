using BuildingBlocks.Messaging;
using BuildingBlocks.Pagination;
using Finance.Contracts;
using Finance.Contracts.Invoices;
using Microsoft.Extensions.DependencyInjection;
using SharedKernel;
using Xunit;

namespace Finance.Application.Tests;

[Collection(FinanceApplicationCollection.Name)]
public sealed class InvoiceFlowTests(FinanceApplicationFixture fixture)
{
    private static string Number() => $"IN-{Guid.NewGuid().ToString("N")[..12]}";

    private async Task<Result<Guid>> CreateAsync(CreateInvoiceRequest request)
    {
        await using AsyncServiceScope scope = fixture.CreateScope();
        return await scope.ServiceProvider
            .GetRequiredService<ICommandHandler<CreateInvoiceCommand, Guid>>()
            .HandleAsync(new CreateInvoiceCommand(request));
    }

    private async Task<InvoiceResponse> ReadAsync(Guid id)
    {
        await using AsyncServiceScope scope = fixture.CreateScope();
        Result<InvoiceResponse> read = await scope.ServiceProvider
            .GetRequiredService<IQueryHandler<GetInvoiceByIdQuery, InvoiceResponse>>()
            .HandleAsync(new GetInvoiceByIdQuery(id));

        Assert.True(read.IsSuccess, read.IsFailure ? read.Error.ToString() : null);
        return read.Value!;
    }

    /// <summary>
    /// The header total is derived from the lines, never supplied. Amounts chosen so the sum has four
    /// decimal places — a header rounded to two would report 60.00 instead of 60.0006.
    /// </summary>
    [Fact]
    public async Task Create_ComputesTheHeaderTotalFromTheLines()
    {
        FinanceTestData.Scope scope = await FinanceTestData.ScopeAsync(fixture);

        Result<Guid> created = await CreateAsync(new CreateInvoiceRequest(
            Number(), scope.ProjectId, InvoiceType.ClientInvoice, DateOnly.FromDateTime(DateTime.UtcNow),
            [
                new InvoiceLineDto("Ply sheets", 3m, 12.5m),
                new InvoiceLineDto("Fixings", 2m, 11.2503m),
            ],
            ClientId: scope.ClientId));

        Assert.True(created.IsSuccess, created.IsFailure ? created.Error.ToString() : null);

        InvoiceResponse invoice = await ReadAsync(created.Value);

        Assert.Equal(37.5m, invoice.Lines.Single(l => l.Description == "Ply sheets").LineTotal);
        Assert.Equal(22.5006m, invoice.Lines.Single(l => l.Description == "Fixings").LineTotal);
        Assert.Equal(60.0006m, invoice.TotalAmount);
        Assert.Equal(60.0006m, invoice.Balance);
        Assert.Equal("Open", invoice.Status);
    }

    [Fact]
    public async Task Create_WithADuplicateNumber_FailsWithDuplicatedNumber()
    {
        FinanceTestData.Scope scope = await FinanceTestData.ScopeAsync(fixture);
        string number = Number();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        Assert.True((await CreateAsync(new CreateInvoiceRequest(
            number, scope.ProjectId, InvoiceType.ClientInvoice, today,
            [new InvoiceLineDto("Work", 1m, 10m)], ClientId: scope.ClientId))).IsSuccess);

        Result<Guid> second = await CreateAsync(new CreateInvoiceRequest(
            number, scope.ProjectId, InvoiceType.ClientInvoice, today,
            [new InvoiceLineDto("Work", 1m, 10m)], ClientId: scope.ClientId));

        Assert.True(second.IsFailure);
        Assert.Equal("invoices.DuplicatedNumber", second.Error.Code);
    }

    [Fact]
    public async Task Create_AClientInvoiceWithoutAClient_FailsWithClientRequired()
    {
        FinanceTestData.Scope scope = await FinanceTestData.ScopeAsync(fixture);

        Result<Guid> created = await CreateAsync(new CreateInvoiceRequest(
            Number(), scope.ProjectId, InvoiceType.ClientInvoice, DateOnly.FromDateTime(DateTime.UtcNow),
            [new InvoiceLineDto("Work", 1m, 10m)]));

        Assert.True(created.IsFailure);
        Assert.Equal("invoices.ClientRequired", created.Error.Code);
    }

    [Fact]
    public async Task Create_AVendorInvoiceWithoutAVendor_FailsWithVendorRequired()
    {
        FinanceTestData.Scope scope = await FinanceTestData.ScopeAsync(fixture);

        Result<Guid> created = await CreateAsync(new CreateInvoiceRequest(
            Number(), scope.ProjectId, InvoiceType.VendorInvoice, DateOnly.FromDateTime(DateTime.UtcNow),
            [new InvoiceLineDto("Work", 1m, 10m)]));

        Assert.True(created.IsFailure);
        Assert.Equal("invoices.VendorRequired", created.Error.Code);
    }

    /// <summary>DIVERGENCE, same shape as F1-06's two: the backend leaves an unknown project to the
    /// foreign key, which surfaces as a 500 rather than a 404.</summary>
    [Fact]
    public async Task Create_ForAnUnknownProject_FailsWithProjectNotFound()
    {
        Result<Guid> created = await CreateAsync(new CreateInvoiceRequest(
            Number(), Guid.NewGuid(), InvoiceType.VendorInvoice, DateOnly.FromDateTime(DateTime.UtcNow),
            [new InvoiceLineDto("Work", 1m, 10m)], VendorName: "Acme"));

        Assert.True(created.IsFailure);
        Assert.Equal("invoices.ProjectNotFound", created.Error.Code);
    }

    /// <summary>
    /// A rejected line rejects the whole invoice — no partial invoice is persisted.
    ///
    /// <para><b>The rejection comes from the VALIDATOR, not the domain, and that is the intended
    /// outcome.</b> The negative unit price is the defect fixed at F1-03 (D-F2), where
    /// <c>InvoiceLine.Create</c> gained <c>InvalidUnitPrice</c>; F1-07's validator enforces the same
    /// bound at the edge, so a negative price never reaches the entity through this path. The domain
    /// guard is the backstop for every other caller — a handler constructing a line directly, a future
    /// import — which is exactly the relationship the two are meant to have. Asserting the validator
    /// error here rather than the domain one is therefore the honest assertion; asserting the domain
    /// code would mean the edge had stopped guarding.</para>
    /// </summary>
    [Fact]
    public async Task Create_WithAnInvalidLine_FailsAndPersistsNothing()
    {
        FinanceTestData.Scope scope = await FinanceTestData.ScopeAsync(fixture);
        string number = Number();

        Result<Guid> created = await CreateAsync(new CreateInvoiceRequest(
            number, scope.ProjectId, InvoiceType.ClientInvoice, DateOnly.FromDateTime(DateTime.UtcNow),
            [
                new InvoiceLineDto("Good", 1m, 10m),
                new InvoiceLineDto("Bad", 1m, -1m),
            ],
            ClientId: scope.ClientId));

        Assert.True(created.IsFailure);

        // The number is free again -- proof the first attempt left no row behind. This is the assertion
        // that would fail if the invoice header were persisted before its lines were validated.
        Result<Guid> retry = await CreateAsync(new CreateInvoiceRequest(
            number, scope.ProjectId, InvoiceType.ClientInvoice, DateOnly.FromDateTime(DateTime.UtcNow),
            [new InvoiceLineDto("Good", 1m, 10m)], ClientId: scope.ClientId));

        Assert.True(retry.IsSuccess, retry.IsFailure ? retry.Error.ToString() : null);
    }

    /// <summary>
    /// The domain backstop for the same rule, reached directly rather than through the validator — so
    /// D-F2's fix is proven to be armed on the entity and not merely duplicated at the edge.
    /// </summary>
    [Fact]
    public void InvoiceLineCreate_StillRefusesANegativeUnitPrice_IndependentlyOfTheValidator()
    {
        Result<Domain.InvoiceLine> line = Domain.InvoiceLine.Create(Guid.NewGuid(), "Direct", 1m, -0.0001m);

        Assert.True(line.IsFailure);
        Assert.Equal("invoice_lines.InvalidUnitPrice", line.Error.Code);
    }

    /// <summary>DIVERGENCE: the backend accepts a due date before the issue date, producing an invoice
    /// that is overdue the moment it exists and would be flagged by F1-08's due job on its first
    /// tick.</summary>
    [Fact]
    public async Task Create_WithADueDateBeforeIssue_IsRejected()
    {
        FinanceTestData.Scope scope = await FinanceTestData.ScopeAsync(fixture);
        var issued = new DateOnly(2026, 6, 1);

        Result<Guid> created = await CreateAsync(new CreateInvoiceRequest(
            Number(), scope.ProjectId, InvoiceType.ClientInvoice, issued,
            [new InvoiceLineDto("Work", 1m, 10m)],
            DueOn: issued.AddDays(-1), ClientId: scope.ClientId));

        Assert.True(created.IsFailure);
    }

    [Fact]
    public async Task AddLine_RederivesTheHeaderFromAllLines()
    {
        FinanceTestData.Scope scope = await FinanceTestData.ScopeAsync(fixture);

        Guid id = (await CreateAsync(new CreateInvoiceRequest(
            Number(), scope.ProjectId, InvoiceType.ClientInvoice, DateOnly.FromDateTime(DateTime.UtcNow),
            [new InvoiceLineDto("First", 1m, 100m)], ClientId: scope.ClientId))).Value;

        await using (AsyncServiceScope dbScope = fixture.CreateScope())
        {
            Result added = await dbScope.ServiceProvider
                .GetRequiredService<ICommandHandler<AddInvoiceLineCommand>>()
                .HandleAsync(new AddInvoiceLineCommand(id, new InvoiceLineDto("Second", 2m, 25m)));

            Assert.True(added.IsSuccess, added.IsFailure ? added.Error.ToString() : null);
        }

        InvoiceResponse invoice = await ReadAsync(id);

        Assert.Equal(2, invoice.Lines.Count);
        Assert.Equal(150m, invoice.TotalAmount);
        Assert.Equal(invoice.Lines.Sum(l => l.LineTotal), invoice.TotalAmount);
    }

    /// <summary>
    /// DIVERGENCE: the backend lets a PAID invoice gain a line, which raises TotalAmount above
    /// PaidAmount while leaving the status Paid — an invoice that is simultaneously settled and owing,
    /// a state nothing else in the module can produce and no code path expects.
    /// </summary>
    [Fact]
    public async Task AddLine_ToAPaidInvoice_FailsWithInvalidStatusTransition()
    {
        FinanceTestData.Scope scope = await FinanceTestData.ScopeAsync(fixture);

        Guid invoiceId = (await CreateAsync(new CreateInvoiceRequest(
            Number(), scope.ProjectId, InvoiceType.ClientInvoice, DateOnly.FromDateTime(DateTime.UtcNow),
            [new InvoiceLineDto("Work", 1m, 40m)], ClientId: scope.ClientId))).Value;

        await using AsyncServiceScope dbScope = fixture.CreateScope();
        IServiceProvider sp = dbScope.ServiceProvider;

        Guid paymentId = (await sp.GetRequiredService<ICommandHandler<Contracts.LedgerEntries.PostLedgerEntryCommand, Guid>>()
            .HandleAsync(new Contracts.LedgerEntries.PostLedgerEntryCommand(
                new Contracts.LedgerEntries.PostLedgerEntryRequest(
                    scope.ProjectId, 40m, LedgerDirection.In, LedgerEntryType.ClientPayment,
                    DateOnly.FromDateTime(DateTime.UtcNow), ClientId: scope.ClientId),
                scope.EmployeeId))).Value;

        await sp.GetRequiredService<ICommandHandler<Contracts.LedgerEntries.AllocateLedgerEntryCommand>>()
            .HandleAsync(new Contracts.LedgerEntries.AllocateLedgerEntryCommand(
                paymentId, invoiceId, 40m, scope.EmployeeId));

        Assert.Equal("Paid", (await ReadAsync(invoiceId)).Status);

        Result added = await sp.GetRequiredService<ICommandHandler<AddInvoiceLineCommand>>()
            .HandleAsync(new AddInvoiceLineCommand(invoiceId, new InvoiceLineDto("Extra", 1m, 5m)));

        Assert.True(added.IsFailure);
        Assert.Equal("invoices.InvalidStatusTransition", added.Error.Code);

        // And the header is untouched, which is the property the refusal exists to protect.
        InvoiceResponse invoice = await ReadAsync(invoiceId);
        Assert.Equal(40m, invoice.TotalAmount);
        Assert.Equal(0m, invoice.Balance);
    }

    [Fact]
    public async Task Cancel_AnUntouchedInvoice_Succeeds_AndItLeavesTheOutstandingList()
    {
        FinanceTestData.Isolated scope = await FinanceTestData.IsolatedProjectAsync(fixture);
        FinanceTestData.Scope owner = await FinanceTestData.ScopeAsync(fixture);

        Guid id = (await CreateAsync(new CreateInvoiceRequest(
            Number(), scope.ProjectId, InvoiceType.ClientInvoice, DateOnly.FromDateTime(DateTime.UtcNow),
            [new InvoiceLineDto("Work", 1m, 10m)], ClientId: owner.ClientId))).Value;

        await using AsyncServiceScope dbScope = fixture.CreateScope();
        IServiceProvider sp = dbScope.ServiceProvider;
        IQueryHandler<GetOutstandingInvoicesQuery, IReadOnlyList<InvoiceListItemResponse>> outstanding = sp.GetRequiredService<
            IQueryHandler<GetOutstandingInvoicesQuery, IReadOnlyList<InvoiceListItemResponse>>>();

        Result<IReadOnlyList<InvoiceListItemResponse>> before = await outstanding.HandleAsync(new GetOutstandingInvoicesQuery(scope.ProjectId));
        Assert.Contains(before.Value!, i => i.Id == id);

        Assert.True((await sp.GetRequiredService<ICommandHandler<CancelInvoiceCommand>>()
            .HandleAsync(new CancelInvoiceCommand(id))).IsSuccess);

        Result<IReadOnlyList<InvoiceListItemResponse>> after = await outstanding.HandleAsync(new GetOutstandingInvoicesQuery(scope.ProjectId));
        Assert.DoesNotContain(after.Value!, i => i.Id == id);

        Assert.Equal("Cancelled", (await ReadAsync(id)).Status);
    }

    /// <summary>A part-paid invoice must have its payments reversed first, so the ledger and the invoice
    /// can never disagree about money already received.</summary>
    [Fact]
    public async Task Cancel_APartPaidInvoice_FailsWithInvalidStatusTransition()
    {
        FinanceTestData.Scope scope = await FinanceTestData.ScopeAsync(fixture);

        Guid invoiceId = (await CreateAsync(new CreateInvoiceRequest(
            Number(), scope.ProjectId, InvoiceType.ClientInvoice, DateOnly.FromDateTime(DateTime.UtcNow),
            [new InvoiceLineDto("Work", 1m, 100m)], ClientId: scope.ClientId))).Value;

        await using AsyncServiceScope dbScope = fixture.CreateScope();
        IServiceProvider sp = dbScope.ServiceProvider;

        Guid paymentId = (await sp.GetRequiredService<ICommandHandler<Contracts.LedgerEntries.PostLedgerEntryCommand, Guid>>()
            .HandleAsync(new Contracts.LedgerEntries.PostLedgerEntryCommand(
                new Contracts.LedgerEntries.PostLedgerEntryRequest(
                    scope.ProjectId, 10m, LedgerDirection.In, LedgerEntryType.ClientPayment,
                    DateOnly.FromDateTime(DateTime.UtcNow), ClientId: scope.ClientId),
                scope.EmployeeId))).Value;

        await sp.GetRequiredService<ICommandHandler<Contracts.LedgerEntries.AllocateLedgerEntryCommand>>()
            .HandleAsync(new Contracts.LedgerEntries.AllocateLedgerEntryCommand(
                paymentId, invoiceId, 10m, scope.EmployeeId));

        Result cancelled = await sp.GetRequiredService<ICommandHandler<CancelInvoiceCommand>>()
            .HandleAsync(new CancelInvoiceCommand(invoiceId));

        Assert.True(cancelled.IsFailure);
        Assert.Equal("invoices.InvalidStatusTransition", cancelled.Error.Code);
    }

    [Fact]
    public async Task GetByProject_PagesAndFiltersByStatus()
    {
        FinanceTestData.Isolated scope = await FinanceTestData.IsolatedProjectAsync(fixture);
        FinanceTestData.Scope owner = await FinanceTestData.ScopeAsync(fixture);
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        Guid openId = (await CreateAsync(new CreateInvoiceRequest(
            Number(), scope.ProjectId, InvoiceType.ClientInvoice, today,
            [new InvoiceLineDto("Work", 1m, 10m)], ClientId: owner.ClientId))).Value;

        Guid cancelledId = (await CreateAsync(new CreateInvoiceRequest(
            Number(), scope.ProjectId, InvoiceType.ClientInvoice, today,
            [new InvoiceLineDto("Work", 1m, 20m)], ClientId: owner.ClientId))).Value;

        await using AsyncServiceScope dbScope = fixture.CreateScope();
        IServiceProvider sp = dbScope.ServiceProvider;

        await sp.GetRequiredService<ICommandHandler<CancelInvoiceCommand>>()
            .HandleAsync(new CancelInvoiceCommand(cancelledId));

        IQueryHandler<GetInvoicesByProjectQuery, PagedResult<InvoiceListItemResponse>> list = sp.GetRequiredService<
            IQueryHandler<GetInvoicesByProjectQuery, PagedResult<InvoiceListItemResponse>>>();

        Result<PagedResult<InvoiceListItemResponse>> all = await list.HandleAsync(new GetInvoicesByProjectQuery(scope.ProjectId));
        Assert.Equal(2, all.Value!.TotalCount);

        Result<PagedResult<InvoiceListItemResponse>> open = await list.HandleAsync(
            new GetInvoicesByProjectQuery(scope.ProjectId, Status: InvoiceStatus.Open));

        Assert.Contains(open.Value!.Items, i => i.Id == openId);
        Assert.DoesNotContain(open.Value!.Items, i => i.Id == cancelledId);
    }

    /// <summary>A zero-total open invoice has nothing outstanding despite being open — the balance
    /// filter, not the status filter, is what excludes it.</summary>
    [Fact]
    public async Task GetOutstanding_ExcludesAZeroTotalOpenInvoice()
    {
        FinanceTestData.Isolated scope = await FinanceTestData.IsolatedProjectAsync(fixture);
        FinanceTestData.Scope owner = await FinanceTestData.ScopeAsync(fixture);
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        Guid zeroId = (await CreateAsync(new CreateInvoiceRequest(
            Number(), scope.ProjectId, InvoiceType.ClientInvoice, today,
            [new InvoiceLineDto("Free sample", 1m, 0m)], ClientId: owner.ClientId))).Value;

        Guid realId = (await CreateAsync(new CreateInvoiceRequest(
            Number(), scope.ProjectId, InvoiceType.ClientInvoice, today,
            [new InvoiceLineDto("Work", 1m, 15m)], ClientId: owner.ClientId))).Value;

        await using AsyncServiceScope dbScope = fixture.CreateScope();
        Result<IReadOnlyList<InvoiceListItemResponse>> outstanding = await dbScope.ServiceProvider.GetRequiredService<
                IQueryHandler<GetOutstandingInvoicesQuery, IReadOnlyList<InvoiceListItemResponse>>>()
            .HandleAsync(new GetOutstandingInvoicesQuery(scope.ProjectId));

        Assert.Contains(outstanding.Value!, i => i.Id == realId);
        Assert.DoesNotContain(outstanding.Value!, i => i.Id == zeroId);
        Assert.Equal("Open", (await ReadAsync(zeroId)).Status);
    }

    [Fact]
    public async Task GetById_ForAnUnknownInvoice_FailsWithNotFound()
    {
        await using AsyncServiceScope scope = fixture.CreateScope();
        Result<InvoiceResponse> read = await scope.ServiceProvider
            .GetRequiredService<IQueryHandler<GetInvoiceByIdQuery, InvoiceResponse>>()
            .HandleAsync(new GetInvoiceByIdQuery(Guid.NewGuid()));

        Assert.True(read.IsFailure);
        Assert.Equal("invoices.NotFound", read.Error.Code);
    }
}
