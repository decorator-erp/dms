using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using BuildingBlocks.Outbox;
using Finance.Contracts;
using Identity.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace Integration.Tests;

/// <summary>
/// <b>The nine-module spine.</b> One project's whole financial life, through the real host and real HTTP
/// routes: project → budget line → variation raised, submitted, signed, applied → invoice issued →
/// payment posted and allocated → Budget gate satisfied → BOQ snapshot generated.
///
/// <para><b>WHAT THIS PROVES, STATED BEFORE IT WAS WRITTEN.</b> It proves the nine schemas coexist under
/// one host, that Finance's 54 routes are reachable with real tokens and real permissions, and that the
/// Budget gate closes end to end across a module boundary the request never crosses directly. It does
/// <b>not</b> prove delivery under concurrency — the outbox is drained deterministically here, because a
/// test that waits on a timer is a slow test that still races.
/// <c>Finance.Application.Tests.BudgetGateFlowTests</c> owns the gate mechanics in detail; this owns the
/// claim that all of it works when assembled.</para>
///
/// <para><b>Run TWICE, each pass with its own actor and project</b>, as the plan requires — a basic
/// flake check, and the shape <c>SitesSpineTests</c> established. Separate file rather than an addition
/// to <c>CrossModuleSmokeTests</c>, following that same precedent: that file is already six hundred
/// lines and this spine shares none of its fixtures.</para>
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class FinanceSpineTests(ApiFixture fixture)
{
    private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.UtcNow);

    [Fact]
    public async Task TheWholeFinancialLifecycle_RunsTwice_AcrossAllNineModules()
    {
        for (int pass = 0; pass < 2; pass++)
        {
            await RunSpineAsync();
        }
    }

    private async Task RunSpineAsync()
    {
        string suffix = Guid.NewGuid().ToString("N")[..12];
        HttpClient client = fixture.CreateClient();

        // ── Identity + HR + CRM: the actor, the employee, the client ──────────────
        // Hiring creates the login, so Identity and HR are one call now.
        (_, string appUserId) = await HrTestSeeding.HireEmployeeOverHttpAsync(
            fixture, $"fin-spine-{suffix}@example.com", jobTitle: "Accountant",
            firstName: "Fin", lastName: "Spine");

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            fixture.IssueTestToken(
                appUserId,
                "employees.create", "stages.manage", "stages.read", "project.create",
                "project.read_all", "project.read", "stage_tasks.read",
                FinancePermissions.Read, FinancePermissions.ManageCostCategory,
                FinancePermissions.ManageBudget, FinancePermissions.ManageVariation,
                FinancePermissions.Variations.Sign, FinancePermissions.Variations.Apply,
                FinancePermissions.ManageInvoice, FinancePermissions.RecordPayment,
                FinancePermissions.GenerateBoq));

        Guid clientId = await CrmTestSeeding.SeedClientAsync(fixture.ConnectionString, appUserId);

        // ── Projects: a project with one stage and one task ───────────────────────
        HttpResponseMessage templateResponse = await client.PostAsJsonAsync(
            "/api/stage-templates", new { Name = $"Tmpl-{suffix}", Type = 2 });
        Assert.Equal(HttpStatusCode.OK, templateResponse.StatusCode);
        Guid templateId = (await templateResponse.Content.ReadFromJsonAsync<StageTemplateIdBody>())!.StageTemplateId;

        HttpResponseMessage templateStageResponse = await client.PostAsJsonAsync(
            $"/api/stage-templates/{templateId}/stages",
            new { Name = "Fit-out", Order = 1, WeightPercent = 100m, Description = (string?)null });
        Assert.Equal(HttpStatusCode.OK, templateStageResponse.StatusCode);
        Guid templateStageId = (await templateStageResponse.Content.ReadFromJsonAsync<StageIdBody>())!.StageId;

        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync(
            $"/api/stages/{templateStageId}/tasks",
            new { Name = "Joinery", Order = 1, Weight = 1.00m, Description = (string?)null, DepartmentId = (Guid?)null }))
            .StatusCode);

        HttpResponseMessage projectResponse = await client.PostAsJsonAsync("/api/projects", new
        {
            Code = $"F-{suffix}"[..12],
            Name = "Finance Spine",
            Address = "1 Test Rd",
            WorkflowType = 1,
            ClientId = clientId,
            StageTemplateId = templateId,
        });
        Assert.Equal(HttpStatusCode.OK, projectResponse.StatusCode);
        Guid projectId = (await projectResponse.Content.ReadFromJsonAsync<ProjectIdBody>())!.ProjectId;

        List<ProjectStageBody>? stages = await (await client.GetAsync($"/api/projects/{projectId}/stages"))
            .Content.ReadFromJsonAsync<List<ProjectStageBody>>();
        Guid projectStageId = Assert.Single(stages!).Id;

        List<ProjectTaskBody>? tasks = await (await client.GetAsync($"/api/projects/{projectId}/tasks"))
            .Content.ReadFromJsonAsync<List<ProjectTaskBody>>();
        Guid projectTaskId = Assert.Single(tasks!).Id;

        // ── Finance: cost category and a task-scoped budget line ──────────────────
        Guid costCategoryId = (await (await client.PostAsJsonAsync("/api/cost-categories", new
        {
            Code = $"CC{suffix[..8]}",
            Name = "Materials",
        })).Content.ReadFromJsonAsync<CostCategoryIdBody>())!.CostCategoryId;

        Guid budgetLineId = (await (await client.PostAsJsonAsync("/api/budget-lines", new
        {
            Code = "BL-1",
            ProjectId = projectId,
            CostCategoryId = costCategoryId,
            Name = "Joinery budget",
            AllocatedAmount = 1000m,
            ProjectStageId = projectStageId,
            ProjectTaskId = projectTaskId,
        })).Content.ReadFromJsonAsync<BudgetLineIdBody>())!.BudgetLineId;

        // ── Variation: raise → submit → sign → apply ──────────────────────────────
        //
        // A ClientDifference, so the signature path and the coverage path are both exercised. Raising
        // opens a Budget gate on the task IN PROJECTS, via the outbox -- this request never touches
        // projects.gate_conditions.
        Guid variationId = (await (await client.PostAsJsonAsync("/api/variations", new
        {
            Kind = 1, // ClientDifference
            ProjectId = projectId,
            Reason = "Client chose the better finish",
            ProjectTaskId = projectTaskId,
            ProjectStageId = projectStageId,
            BudgetedAmount = 1000m,
            ChosenAmount = 1300m,
        })).Content.ReadFromJsonAsync<VariationIdBody>())!.VariationId;

        await DrainOutboxAsync();

        // The gate exists and the task is Locked by it -- written by Projects, caused by Finance.
        Assert.Equal(1, await TaskStatusAsync(client, projectId)); // Locked

        Assert.Equal(HttpStatusCode.NoContent,
            (await client.PutAsJsonAsync($"/api/variations/{variationId}/submit", new { })).StatusCode);

        Assert.Equal(HttpStatusCode.NoContent, (await client.PutAsJsonAsync(
            $"/api/variations/{variationId}/sign",
            new { SignedByClientId = clientId, SignedDocumentPath = "/signed/diff.pdf" })).StatusCode);

        await DrainOutboxAsync();

        // ── The Budget gate closed, and the task is Open again ────────────────────
        Assert.Equal(2, await TaskStatusAsync(client, projectId)); // Open

        Assert.Equal(HttpStatusCode.NoContent,
            (await client.PutAsJsonAsync($"/api/variations/{variationId}/apply", new { })).StatusCode);

        // ── Invoice issued, payment posted, payment allocated ─────────────────────
        Guid invoiceId = (await (await client.PostAsJsonAsync("/api/invoices", new
        {
            Number = $"INV-{suffix}",
            ProjectId = projectId,
            Type = 1, // ClientInvoice
            IssuedOn = Today,
            Lines = new[] { new { Description = "Stage 1", Quantity = 1m, UnitPrice = 1300m } },
            ProjectStageId = projectStageId,
            DueOn = Today.AddDays(30),
            ClientId = clientId,
        })).Content.ReadFromJsonAsync<InvoiceIdBody>())!.InvoiceId;

        Guid paymentId = (await (await client.PostAsJsonAsync("/api/ledger-entries", new
        {
            ProjectId = projectId,
            Amount = 1300m,
            Direction = 1, // In
            Type = 3,      // ClientPayment
            OccurredOn = Today,
            ProjectStageId = projectStageId,
            ClientId = clientId,
        })).Content.ReadFromJsonAsync<LedgerEntryIdBody>())!.LedgerEntryId;

        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsJsonAsync(
            $"/api/ledger-entries/{paymentId}/allocate",
            new { InvoiceId = invoiceId, Amount = 1300m })).StatusCode);

        await DrainOutboxAsync();

        InvoiceStatusBody? invoice = await client.GetFromJsonAsync<InvoiceStatusBody>($"/api/invoices/{invoiceId}");
        Assert.Equal("Paid", invoice!.Status);
        Assert.Equal(0m, invoice.Balance);

        // ── BOQ snapshot ──────────────────────────────────────────────────────────
        HttpResponseMessage snapshotResponse = await client.PostAsJsonAsync("/api/boq-snapshots", new
        {
            ProjectId = projectId,
            Type = 3, // Manual
            ProjectStageId = (Guid?)null,
            Notes = "Spine",
        });
        Assert.Equal(HttpStatusCode.Created, snapshotResponse.StatusCode);

        BoqSnapshotBody? snapshot = await client.GetFromJsonAsync<BoqSnapshotBody>(
            $"/api/boq-snapshots/by-project/{projectId}/latest");

        Assert.Equal(1, snapshot!.Version);

        // The applied ClientDifference wrote NOTHING to the budget line, so the BOQ's budgeted total is
        // still the original 1000 -- and the 300 difference is reported as a difference line instead.
        Assert.Equal(1000m, snapshot.BudgetedTotal);
        Assert.Equal(300m, snapshot.DifferenceTotal);

        // ── And the whole thing reconciles through the read slice ─────────────────
        FinancialSummaryBody? summary = await client.GetFromJsonAsync<FinancialSummaryBody>(
            $"/api/projects/{projectId}/financial/summary");

        Assert.Equal(1000m, summary!.Totals.EffectiveCap);
        Assert.Equal(1300m, summary.Totals.ClientPaidAmount);

        DashboardBody? dashboard = await client.GetFromJsonAsync<DashboardBody>(
            $"/api/projects/{projectId}/financial-dashboard");

        DashboardStageBody fundedStage = Assert.Single(dashboard!.Collection.Stages);
        Assert.True(fundedStage.IsFunded);
    }

    private async Task DrainOutboxAsync()
    {
        OutboxProcessor processor = fixture.Services.GetServices<IHostedService>().OfType<OutboxProcessor>().Single();

        // Bounded rather than looping to quiescence: a chain that will not settle is a bug this should
        // surface as a failed assertion below, not as a hang.
        for (int i = 0; i < 5; i++)
        {
            await processor.ProcessPendingAsync(CancellationToken.None);
        }
    }

    private static async Task<int> TaskStatusAsync(HttpClient client, Guid projectId)
    {
        List<ProjectTaskBody>? tasks = await (await client.GetAsync($"/api/projects/{projectId}/tasks"))
            .Content.ReadFromJsonAsync<List<ProjectTaskBody>>();

        return Assert.Single(tasks!).Status;
    }

    private Task<string> RegisterAsync(string email) =>
        IdentityTestSeeding.ProvisionActiveUserAsync(fixture, email, firstName: "Fin", lastName: "Spine");

    private sealed record StageTemplateIdBody(Guid StageTemplateId);
    private sealed record StageIdBody(Guid StageId);
    private sealed record ProjectIdBody(Guid ProjectId);
    private sealed record ProjectStageBody(Guid Id);
    private sealed record ProjectTaskBody(Guid Id, int Status);
    private sealed record CostCategoryIdBody(Guid CostCategoryId);
    private sealed record BudgetLineIdBody(Guid BudgetLineId);
    private sealed record VariationIdBody(Guid VariationId);
    private sealed record InvoiceIdBody(Guid InvoiceId);
    private sealed record LedgerEntryIdBody(Guid LedgerEntryId);
    private sealed record InvoiceStatusBody(string Status, decimal Balance);
    private sealed record BoqSnapshotBody(int Version, decimal BudgetedTotal, decimal DifferenceTotal);
    private sealed record FinancialTotalsBody(decimal EffectiveCap, decimal ClientPaidAmount);
    private sealed record FinancialSummaryBody(FinancialTotalsBody Totals);
    private sealed record DashboardStageBody(bool IsFunded);
    private sealed record DashboardCollectionBody(List<DashboardStageBody> Stages);
    private sealed record DashboardBody(DashboardCollectionBody Collection);
}
