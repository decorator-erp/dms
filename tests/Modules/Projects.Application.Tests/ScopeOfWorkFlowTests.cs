using BuildingBlocks.Messaging;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Projects.Contracts;
using Projects.Contracts.Scope;
using Projects.Domain;
using Projects.Infrastructure.Persistence;
using SharedKernel;
using Xunit;
using ScopeOfWorkStatus = Projects.Contracts.ScopeOfWorkStatus;

namespace Projects.Application.Tests;

/// <summary>SOW-4: picking, editing and reading a scope of work through the real handlers, the validation and
/// scope decorators, and Postgres. Confirm is <c>ScopeOfWorkConfirmTests</c>.</summary>
[Collection(ProjectsApplicationCollection.Name)]
public sealed class ScopeOfWorkFlowTests(ProjectsApplicationFixture fixture)
{
    [Fact]
    public async Task Picking_from_two_templates_copies_name_description_and_fee()
    {
        ScopeTestProject p = await ScopeOfWorkSeeding.CreateProjectAsync(fixture, "pick-two");
        (_, IReadOnlyList<SeededStage> a) = await ScopeOfWorkSeeding.CreateTemplateAsync(fixture, "Alpha", 1200m, null);
        (_, IReadOnlyList<SeededStage> b) = await ScopeOfWorkSeeding.CreateTemplateAsync(fixture, "Beta", 300.50m);

        Result<IReadOnlyList<ScopeOfWorkItemResponse>> added = await AddAsync(p, b[0].Id, a[1].Id, a[0].Id);

        Assert.True(added.IsSuccess, added.IsFailure ? added.Error.ToString() : null);
        ScopeOfWorkResponse scope = await GetAsync(p);
        Assert.Equal([b[0].Id, a[1].Id, a[0].Id], scope.Items.Select(i => i.SourceStageId));
        Assert.Equal([1, 2, 3], scope.Items.Select(i => i.Order));
        Assert.Equal([300.50m, 0m, 1200m], scope.Items.Select(i => i.Fee));
        Assert.Equal(b[0].Name, scope.Items[0].Name);
        Assert.Equal($"{b[0].Name} description", scope.Items[0].Description);
        Assert.StartsWith("Beta-", scope.Items[0].SourceTemplateName);
        Assert.StartsWith("Alpha-", scope.Items[1].SourceTemplateName);
        Assert.Equal(1500.50m, scope.Subtotal);
        Assert.Equal(ScopeOfWorkStatus.Draft, scope.Status);
    }

    [Fact]
    public async Task A_duplicate_pick_is_409_and_adding_a_whole_template_skips_stages_already_picked()
    {
        ScopeTestProject p = await ScopeOfWorkSeeding.CreateProjectAsync(fixture, "pick-dup");
        (Guid templateId, IReadOnlyList<SeededStage> stages) = await ScopeOfWorkSeeding.CreateTemplateAsync(fixture, "Dup", 10m, 20m, 30m);
        Assert.True((await AddAsync(p, stages[1].Id)).IsSuccess);

        Result<IReadOnlyList<ScopeOfWorkItemResponse>> duplicate = await AddAsync(p, stages[1].Id);
        Assert.Equal(ScopeOfWorkErrors.DuplicateItem, duplicate.Error);
        Assert.Equal(409, duplicate.Error.Status);

        Result<IReadOnlyList<ScopeOfWorkItemResponse>> whole = await SendAsync<AddScopeOfWorkTemplateCommand, IReadOnlyList<ScopeOfWorkItemResponse>>(
            p, new AddScopeOfWorkTemplateCommand(p.ProjectId, p.OfficeUserId, templateId));
        Assert.True(whole.IsSuccess, whole.IsFailure ? whole.Error.ToString() : null);
        Assert.Equal([stages[0].Id, stages[2].Id], whole.Value!.Select(i => i.SourceStageId));
        Assert.Equal([2, 3], whole.Value!.Select(i => i.Order));

        Result<IReadOnlyList<ScopeOfWorkItemResponse>> again = await SendAsync<AddScopeOfWorkTemplateCommand, IReadOnlyList<ScopeOfWorkItemResponse>>(
            p, new AddScopeOfWorkTemplateCommand(p.ProjectId, p.OfficeUserId, templateId));
        Assert.True(again.IsSuccess);
        Assert.Empty(again.Value!);
        Assert.Equal(3, (await GetAsync(p)).Items.Count);
    }

    [Fact]
    public async Task A_missing_source_stage_is_SourceStageNotFound()
    {
        ScopeTestProject p = await ScopeOfWorkSeeding.CreateProjectAsync(fixture, "pick-missing");

        Result<IReadOnlyList<ScopeOfWorkItemResponse>> added = await AddAsync(p, Guid.NewGuid());

        Assert.Equal(ScopeOfWorkErrors.SourceStageNotFound, added.Error);
        Assert.Null((await GetAsync(p)).Id);
    }

    [Fact]
    public async Task Items_can_be_edited_removed_with_renumbering_and_reordered()
    {
        ScopeTestProject p = await ScopeOfWorkSeeding.CreateProjectAsync(fixture, "edit");
        (_, IReadOnlyList<SeededStage> s) = await ScopeOfWorkSeeding.CreateTemplateAsync(fixture, "Edit", 1m, 2m, 3m, 4m);
        IReadOnlyList<ScopeOfWorkItemResponse> items = (await AddAsync(p, [.. s.Select(x => x.Id)])).Value!;

        Assert.True((await SendAsync(p, new UpdateScopeOfWorkItemCommand(
            p.ProjectId, p.OfficeUserId, items[0].Id, "  Concept and layout ", "Edited", 999.99m))).IsSuccess);
        Assert.True((await SendAsync(p, new RemoveScopeOfWorkItemCommand(p.ProjectId, p.OfficeUserId, items[1].Id))).IsSuccess);

        ScopeOfWorkResponse afterRemove = await GetAsync(p);
        Assert.Equal([items[0].Id, items[2].Id, items[3].Id], afterRemove.Items.Select(i => i.Id));
        Assert.Equal([1, 2, 3], afterRemove.Items.Select(i => i.Order));
        Assert.Equal(("Concept and layout", "Edited", 999.99m), (afterRemove.Items[0].Name, afterRemove.Items[0].Description, afterRemove.Items[0].Fee));

        Assert.True((await SendAsync(p, new ReorderScopeOfWorkItemsCommand(
            p.ProjectId, p.OfficeUserId, [items[3].Id, items[0].Id, items[2].Id]))).IsSuccess);
        ScopeOfWorkResponse reordered = await GetAsync(p);
        Assert.Equal([items[3].Id, items[0].Id, items[2].Id], reordered.Items.Select(i => i.Id));
        Assert.Equal([1, 2, 3], reordered.Items.Select(i => i.Order));

        Result notTheSet = await SendAsync(p, new ReorderScopeOfWorkItemsCommand(p.ProjectId, p.OfficeUserId, [items[3].Id, items[0].Id]));
        Assert.Equal(ScopeOfWorkErrors.InvalidOrder, notTheSet.Error);
        Assert.Equal(ScopeOfWorkErrors.ItemNotFound,
            (await SendAsync(p, new RemoveScopeOfWorkItemCommand(p.ProjectId, p.OfficeUserId, items[1].Id))).Error);
    }

    [Fact]
    public async Task Get_without_a_scope_is_an_empty_draft_and_writes_nothing_then_the_header_round_trips()
    {
        ScopeTestProject p = await ScopeOfWorkSeeding.CreateProjectAsync(fixture, "header");

        ScopeOfWorkResponse empty = await GetAsync(p);
        Assert.Null(empty.Id);
        Assert.Equal(p.ProjectId, empty.ProjectId);
        Assert.Equal(ScopeOfWorkStatus.Draft, empty.Status);
        Assert.Empty(empty.Items);
        Assert.Equal(0m, empty.Total);
        Assert.Equal(0, await CountScopesAsync(p.ProjectId));

        Assert.True((await SendAsync(p, new UpdateScopeOfWorkHeaderCommand(
            p.ProjectId, p.OfficeUserId, 90, new DateOnly(2026, 12, 1), -150m, " Early-client discount ", "Notes"))).IsSuccess);

        ScopeOfWorkResponse saved = await GetAsync(p);
        Assert.NotNull(saved.Id);
        Assert.Equal(90, saved.DurationDays);
        Assert.Equal(new DateOnly(2026, 12, 1), saved.PlannedStartDate);
        Assert.Equal(new DateOnly(2027, 3, 1), saved.EstimatedEndDate);
        Assert.Equal(-150m, saved.AdjustmentAmount);
        Assert.Equal("Early-client discount", saved.AdjustmentNote);
        Assert.Equal(-150m, saved.Total);
        Assert.Equal(1, await CountScopesAsync(p.ProjectId));
    }

    [Fact]
    public async Task An_employee_outside_the_project_gets_404_on_every_route()
    {
        ScopeTestProject p = await ScopeOfWorkSeeding.CreateProjectAsync(fixture, "outsider");
        (_, IReadOnlyList<SeededStage> s) = await ScopeOfWorkSeeding.CreateTemplateAsync(fixture, "Out", 5m);
        Guid itemId = (await AddAsync(p, s[0].Id)).Value![0].Id;
        string outsider;
        await using (AsyncServiceScope seed = fixture.CreateScope())
        {
            (_, outsider) = await HrTestSeeding.HireEmployeeAsync(seed.ServiceProvider, $"sow-outsider-{Guid.NewGuid():N}@example.com");
        }

        await using AsyncServiceScope scope = fixture.CreateScope();
        IServiceProvider sp = scope.ServiceProvider;
        sp.GetRequiredService<TestCurrentUser>().UserId = outsider;
        Guid projectId = p.ProjectId;

        Error[] errors =
        [
            (await sp.GetRequiredService<IQueryHandler<GetScopeOfWorkQuery, ScopeOfWorkResponse>>()
                .HandleAsync(new GetScopeOfWorkQuery(projectId, outsider))).Error,
            (await sp.GetRequiredService<ICommandHandler<UpdateScopeOfWorkHeaderCommand>>()
                .HandleAsync(new UpdateScopeOfWorkHeaderCommand(projectId, outsider, 10, null, 0m, null, null))).Error,
            (await sp.GetRequiredService<ICommandHandler<AddScopeOfWorkItemsCommand, IReadOnlyList<ScopeOfWorkItemResponse>>>()
                .HandleAsync(new AddScopeOfWorkItemsCommand(projectId, outsider, [s[0].Id]))).Error,
            (await sp.GetRequiredService<ICommandHandler<AddScopeOfWorkTemplateCommand, IReadOnlyList<ScopeOfWorkItemResponse>>>()
                .HandleAsync(new AddScopeOfWorkTemplateCommand(projectId, outsider, Guid.NewGuid()))).Error,
            (await sp.GetRequiredService<ICommandHandler<UpdateScopeOfWorkItemCommand>>()
                .HandleAsync(new UpdateScopeOfWorkItemCommand(projectId, outsider, itemId, "X", null, 1m))).Error,
            (await sp.GetRequiredService<ICommandHandler<RemoveScopeOfWorkItemCommand>>()
                .HandleAsync(new RemoveScopeOfWorkItemCommand(projectId, outsider, itemId))).Error,
            (await sp.GetRequiredService<ICommandHandler<ReorderScopeOfWorkItemsCommand>>()
                .HandleAsync(new ReorderScopeOfWorkItemsCommand(projectId, outsider, [itemId]))).Error,
        ];

        Assert.All(errors, e => Assert.Equal(ProjectErrors.NotFound, e));
        Assert.Single((await GetAsync(p)).Items);
    }

    [Fact]
    public async Task Over_long_name_negative_fee_and_zero_duration_are_400()
    {
        ScopeTestProject p = await ScopeOfWorkSeeding.CreateProjectAsync(fixture, "validate");
        (_, IReadOnlyList<SeededStage> s) = await ScopeOfWorkSeeding.CreateTemplateAsync(fixture, "Val", 5m);
        Guid itemId = (await AddAsync(p, s[0].Id)).Value![0].Id;

        Result longName = await SendAsync(p, new UpdateScopeOfWorkItemCommand(p.ProjectId, p.OfficeUserId, itemId, new string('n', 257), null, 1m));
        Result negativeFee = await SendAsync(p, new UpdateScopeOfWorkItemCommand(p.ProjectId, p.OfficeUserId, itemId, "Ok", null, -1m));
        Result zeroDuration = await SendAsync(p, new UpdateScopeOfWorkHeaderCommand(p.ProjectId, p.OfficeUserId, 0, null, 0m, null, null));
        Result longNote = await SendAsync(p, new UpdateScopeOfWorkHeaderCommand(p.ProjectId, p.OfficeUserId, 10, null, -1m, new string('x', 501), null));

        Assert.All([longName, negativeFee, zeroDuration, longNote], r => Assert.Equal(400, r.Error.Status));
        Assert.Equal(5m, (await GetAsync(p)).Items[0].Fee);
    }

    [Fact]
    public async Task A_concurrent_edit_is_ConcurrentUpdate()
    {
        ScopeTestProject p = await ScopeOfWorkSeeding.CreateProjectAsync(fixture, "concurrent");
        (_, IReadOnlyList<SeededStage> s) = await ScopeOfWorkSeeding.CreateTemplateAsync(fixture, "Con", 5m, 6m);
        IReadOnlyList<ScopeOfWorkItemResponse> items = (await AddAsync(p, s[0].Id, s[1].Id)).Value!;

        // A loads the scope, then B edits a DIFFERENT item and commits; A's save must still be refused.
        await using AsyncServiceScope a = fixture.CreateScope();
        ProjectsDbContext db = a.ServiceProvider.GetRequiredService<ProjectsDbContext>();
        ScopeOfWork stale = await db.ScopesOfWork.Include(x => x.Items).SingleAsync(x => x.ProjectId == p.ProjectId);

        Assert.True((await SendAsync(p, new UpdateScopeOfWorkItemCommand(p.ProjectId, p.OfficeUserId, items[1].Id, "B wins", null, 60m))).IsSuccess);

        Assert.True(stale.UpdateItem(items[0].Id, "A loses", null, 50m).IsSuccess);
        Result result = await ScopeWrites.SaveAsync(db, stale, CancellationToken.None);

        Assert.Equal(ScopeOfWorkErrors.ConcurrentUpdate, result.Error);
        Assert.Equal(409, result.Error.Status);
        ScopeOfWorkResponse current = await GetAsync(p);
        Assert.Equal([5m, 60m], current.Items.Select(i => i.Fee));
    }

    [Fact]
    public async Task Two_first_saves_at_once_leave_one_scope_and_the_loser_gets_ConcurrentUpdate()
    {
        ScopeTestProject p = await ScopeOfWorkSeeding.CreateProjectAsync(fixture, "first-save");

        // First save A, uncommitted: its scope row holds the unique ProjectId slot.
        await using AsyncServiceScope a = fixture.CreateScope();
        ProjectsDbContext db = a.ServiceProvider.GetRequiredService<ProjectsDbContext>();
        await using IDbContextTransaction transaction = await db.Database.BeginTransactionAsync();
        var first = ScopeOfWork.Create(p.ProjectId);
        Assert.True(first.UpdateHeader(30, null, 0m, null, null).IsSuccess);
        db.ScopesOfWork.Add(first);
        await db.SaveChangesAsync();

        // First save B, through the handler: it saw no scope, so it inserts and waits on the index.
        Task<Result> second = SendAsync(p, new UpdateScopeOfWorkHeaderCommand(p.ProjectId, p.OfficeUserId, 60, null, 0m, null, null));
        Assert.NotSame(second, await Task.WhenAny(second, Task.Delay(TimeSpan.FromMilliseconds(500))));

        await transaction.CommitAsync();
        Result result = await second;

        Assert.Equal(ScopeOfWorkErrors.ConcurrentUpdate, result.Error);
        Assert.Equal(1, await CountScopesAsync(p.ProjectId));
        Assert.Equal(30, (await GetAsync(p)).DurationDays);
    }

    [Fact]
    public async Task A_project_that_does_not_exist_is_404_for_a_project_wide_reader()
    {
        var p = new ScopeTestProject(Guid.NewGuid(), (await ScopeOfWorkSeeding.CreateProjectAsync(fixture, "ghost")).OfficeUserId, Guid.Empty);

        Assert.Equal(ProjectErrors.NotFound, (await SendAsync(p, new UpdateScopeOfWorkHeaderCommand(
            p.ProjectId, p.OfficeUserId, 10, null, 0m, null, null))).Error);
        await using AsyncServiceScope scope = ScopeOfWorkSeeding.OfficeScope(fixture, p);
        Result<ScopeOfWorkResponse> read = await scope.ServiceProvider
            .GetRequiredService<IQueryHandler<GetScopeOfWorkQuery, ScopeOfWorkResponse>>()
            .HandleAsync(new GetScopeOfWorkQuery(p.ProjectId, p.OfficeUserId));
        Assert.Equal(ProjectErrors.NotFound, read.Error);
    }

    // --- helpers ---

    private Task<Result<IReadOnlyList<ScopeOfWorkItemResponse>>> AddAsync(ScopeTestProject p, params Guid[] stageIds) =>
        SendAsync<AddScopeOfWorkItemsCommand, IReadOnlyList<ScopeOfWorkItemResponse>>(
            p, new AddScopeOfWorkItemsCommand(p.ProjectId, p.OfficeUserId, stageIds));

    private async Task<Result> SendAsync<TCommand>(ScopeTestProject p, TCommand command)
        where TCommand : ICommand
    {
        await using AsyncServiceScope scope = ScopeOfWorkSeeding.OfficeScope(fixture, p);
        return await scope.ServiceProvider.GetRequiredService<ICommandHandler<TCommand>>().HandleAsync(command);
    }

    private async Task<Result<TResponse>> SendAsync<TCommand, TResponse>(ScopeTestProject p, TCommand command)
        where TCommand : ICommand<TResponse>
    {
        await using AsyncServiceScope scope = ScopeOfWorkSeeding.OfficeScope(fixture, p);
        return await scope.ServiceProvider.GetRequiredService<ICommandHandler<TCommand, TResponse>>().HandleAsync(command);
    }

    private async Task<ScopeOfWorkResponse> GetAsync(ScopeTestProject p)
    {
        await using AsyncServiceScope scope = ScopeOfWorkSeeding.OfficeScope(fixture, p);
        Result<ScopeOfWorkResponse> result = await scope.ServiceProvider
            .GetRequiredService<IQueryHandler<GetScopeOfWorkQuery, ScopeOfWorkResponse>>()
            .HandleAsync(new GetScopeOfWorkQuery(p.ProjectId, p.OfficeUserId));
        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.ToString() : null);
        return result.Value!;
    }

    private async Task<int> CountScopesAsync(Guid projectId)
    {
        await using AsyncServiceScope scope = fixture.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<ProjectsDbContext>().ScopesOfWork.CountAsync(s => s.ProjectId == projectId);
    }
}
