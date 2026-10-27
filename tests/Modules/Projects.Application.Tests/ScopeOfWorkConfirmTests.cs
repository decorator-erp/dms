using System.Text.Json;
using BuildingBlocks.Messaging;
using BuildingBlocks.Outbox;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Projects.Contracts;
using Projects.Contracts.Projects;
using Projects.Contracts.Scope;
using Projects.Contracts.Templates;
using Projects.Domain;
using Projects.Infrastructure.Persistence;
using SharedKernel;
using Xunit;
using WorkflowType = Projects.Contracts.WorkflowType;

namespace Projects.Application.Tests;

/// <summary>SOW-5: confirming a scope of work creates exactly the picked stages through the shared
/// instantiation code, sets the project's dates, locks the scope and announces it once.</summary>
[Collection(ProjectsApplicationCollection.Name)]
public sealed class ScopeOfWorkConfirmTests(ProjectsApplicationFixture fixture)
{
    private static readonly JsonSerializerOptions WebJson = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task Confirm_creates_exactly_the_picked_stages_in_item_order_with_their_tasks_teams_and_fees()
    {
        ScopeTestProject p = await ScopeOfWorkSeeding.CreateProjectAsync(fixture, "core");
        (_, IReadOnlyList<SeededStage> a) = await ScopeOfWorkSeeding.CreateTemplateAsync(fixture, "Core A", 1000m, 700m);
        (_, IReadOnlyList<SeededStage> b) = await ScopeOfWorkSeeding.CreateTemplateAsync(fixture, "Core B", 500m, null);
        Guid departmentId = await ScopeOfWorkSeeding.AddTasksAndDepartmentAsync(fixture, a[0].Id, taskCount: 2);
        await ScopeOfWorkSeeding.AddTasksAndDepartmentAsync(fixture, a[1].Id, taskCount: 3); // never picked

        IReadOnlyList<ScopeOfWorkItemResponse> items = (await AddAsync(p, b[1].Id, a[0].Id, b[0].Id)).Value!;
        Assert.True((await SendAsync(p, new UpdateScopeOfWorkItemCommand(
            p.ProjectId, p.OfficeUserId, items[1].Id, "Design (custom)", "Our wording", 1250m))).IsSuccess);
        await SetHeaderAsync(p, durationDays: 60);

        Result<ConfirmScopeOfWorkResponse> result = await ConfirmAsync(p);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.ToString() : null);
        await using AsyncServiceScope read = fixture.CreateScope();
        ProjectsDbContext db = read.ServiceProvider.GetRequiredService<ProjectsDbContext>();
        List<ProjectStage> stages = await db.ProjectStages.AsNoTracking()
            .Where(s => s.ProjectId == p.ProjectId).OrderBy(s => s.Order).ToListAsync();

        Assert.Equal(3, stages.Count);
        Assert.Equal(result.Value!.CreatedStageIds, stages.Select(s => s.Id));
        Assert.Equal([b[1].Id, a[0].Id, b[0].Id], stages.Select(s => s.StageId!.Value));
        Assert.Equal([1, 2, 3], stages.Select(s => s.Order));
        Assert.Equal([b[1].Name, "Design (custom)", b[0].Name], stages.Select(s => s.Name));
        Assert.Equal("Our wording", stages[1].Description);
        Assert.Equal([0m, 1250m, 500m], stages.Select(s => s.Cost!.Value));

        List<ProjectTask> tasks = await db.ProjectTasks.AsNoTracking().Where(t => t.ProjectId == p.ProjectId).ToListAsync();
        Assert.Equal(2, tasks.Count);
        Assert.All(tasks, t => Assert.Equal(stages[1].Id, t.ProjectStageId));
        Assert.Equal(2, stages[1].TotalTasks);
        var taskIds = tasks.Select(t => t.Id).ToList();
        Assert.Equal(2, await db.ProjectSubTasks.CountAsync(st => taskIds.Contains(st.ProjectTaskId)));
        StageTeam team = await db.StageTeams.AsNoTracking().SingleAsync(t => t.ProjectStageId == stages[1].Id);
        Assert.Equal(departmentId, team.DepartmentId);
        var stageIds = stages.Select(s => s.Id).ToList();
        Assert.Equal(1, await db.StageTeams.CountAsync(t => stageIds.Contains(t.ProjectStageId)));

        ScopeOfWorkResponse scope = await GetAsync(p);
        Assert.Equal(Contracts.ScopeOfWorkStatus.Confirmed, scope.Status);
        Assert.Equal(p.OfficeEmployeeId, scope.ConfirmedById);
        Assert.NotNull(scope.ConfirmedAtUtc);
        Assert.Equal(stages.Select(s => (Guid?)s.Id), scope.Items.Select(i => i.CreatedStageId));
    }

    [Fact]
    public async Task An_item_name_at_the_longest_allowed_length_confirms_into_a_stage()
    {
        ScopeTestProject p = await ScopeOfWorkSeeding.CreateProjectAsync(fixture, "long-name");
        (_, IReadOnlyList<SeededStage> s) = await ScopeOfWorkSeeding.CreateTemplateAsync(fixture, "LongName", 10m);
        Guid itemId = (await AddAsync(p, s[0].Id)).Value![0].Id;
        string longest = new('n', 200);
        Assert.True((await SendAsync(p, new UpdateScopeOfWorkItemCommand(p.ProjectId, p.OfficeUserId, itemId, longest, null, 10m))).IsSuccess);
        await SetHeaderAsync(p, durationDays: 10);

        Result<ConfirmScopeOfWorkResponse> result = await ConfirmAsync(p);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.ToString() : null);
        await using AsyncServiceScope read = fixture.CreateScope();
        Assert.Equal(longest, (await read.ServiceProvider.GetRequiredService<ProjectsDbContext>().ProjectStages.AsNoTracking()
            .SingleAsync(x => x.ProjectId == p.ProjectId)).Name);
    }

    [Fact]
    public async Task Confirm_sets_the_project_dates_from_the_planned_start_at_midnight_utc()
    {
        ScopeTestProject p = await ScopeOfWorkSeeding.CreateProjectAsync(fixture, "dates");
        (_, IReadOnlyList<SeededStage> s) = await ScopeOfWorkSeeding.CreateTemplateAsync(fixture, "Dates", 10m);
        Assert.True((await AddAsync(p, s[0].Id)).IsSuccess);
        await SetHeaderAsync(p, durationDays: 45, plannedStart: new DateOnly(2026, 12, 1));

        Assert.True((await ConfirmAsync(p)).IsSuccess);

        ProjectResponse project = await GetProjectAsync(p);
        Assert.Equal(new DateTime(2026, 12, 1, 0, 0, 0, DateTimeKind.Utc), project.EstimatedStartDate);
        Assert.Equal(new DateTime(2027, 1, 15, 0, 0, 0, DateTimeKind.Utc), project.EstimatedEndDate);
        Assert.Equal(DateTimeKind.Utc, project.EstimatedStartDate!.Value.Kind);
    }

    [Fact]
    public async Task Confirm_without_a_planned_start_leaves_the_project_dates_untouched()
    {
        var start = new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc);
        var end = new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc);
        ScopeTestProject p = await ScopeOfWorkSeeding.CreateProjectAsync(fixture, "no-dates", estimatedStart: start, estimatedEnd: end);
        (_, IReadOnlyList<SeededStage> s) = await ScopeOfWorkSeeding.CreateTemplateAsync(fixture, "NoDates", 10m);
        Assert.True((await AddAsync(p, s[0].Id)).IsSuccess);
        await SetHeaderAsync(p, durationDays: 300);

        Assert.True((await ConfirmAsync(p)).IsSuccess);

        ProjectResponse project = await GetProjectAsync(p);
        Assert.Equal(start, project.EstimatedStartDate);
        Assert.Equal(end, project.EstimatedEndDate);
    }

    [Fact]
    public async Task A_project_that_already_has_stages_is_ProjectHasStages()
    {
        (Guid templateId, _) = await ScopeOfWorkSeeding.CreateTemplateAsync(fixture, "Existing", 1m);
        ScopeTestProject p = await ScopeOfWorkSeeding.CreateProjectAsync(fixture, "has-stages", stageTemplateId: templateId);
        (_, IReadOnlyList<SeededStage> s) = await ScopeOfWorkSeeding.CreateTemplateAsync(fixture, "Other", 2m);
        Assert.True((await AddAsync(p, s[0].Id)).IsSuccess);
        await SetHeaderAsync(p, durationDays: 10);

        Result<ConfirmScopeOfWorkResponse> result = await ConfirmAsync(p);

        Assert.Equal(ScopeOfWorkErrors.ProjectHasStages, result.Error);
        Assert.Equal(409, result.Error.Status);
        Assert.Equal(1, await CountStagesAsync(p.ProjectId));
    }

    [Fact]
    public async Task A_source_stage_deleted_after_picking_is_SourceStageNotFound()
    {
        ScopeTestProject p = await ScopeOfWorkSeeding.CreateProjectAsync(fixture, "deleted-source");
        (_, IReadOnlyList<SeededStage> s) = await ScopeOfWorkSeeding.CreateTemplateAsync(fixture, "Deleted", 1m, 2m);
        Assert.True((await AddAsync(p, s[0].Id, s[1].Id)).IsSuccess);
        await SetHeaderAsync(p, durationDays: 10);
        await using (AsyncServiceScope scope = fixture.CreateScope())
        {
            Assert.True((await scope.ServiceProvider.GetRequiredService<ICommandHandler<DeleteStageCommand>>()
                .HandleAsync(new DeleteStageCommand(s[1].Id))).IsSuccess);
        }

        Result<ConfirmScopeOfWorkResponse> result = await ConfirmAsync(p);

        Assert.Equal(ScopeOfWorkErrors.SourceStageNotFound, result.Error);
        Assert.Equal(400, result.Error.Status);
        Assert.Equal(0, await CountStagesAsync(p.ProjectId));
        Assert.Null((await GetAsync(p)).Items[1].SourceTemplateName);
    }

    [Fact]
    public async Task A_missing_department_head_fails_with_MissingHead_and_creates_nothing()
    {
        ScopeTestProject p = await ScopeOfWorkSeeding.CreateProjectAsync(fixture, "no-head");
        (_, IReadOnlyList<SeededStage> s) = await ScopeOfWorkSeeding.CreateTemplateAsync(fixture, "NoHead", 1m, 2m);
        await ScopeOfWorkSeeding.AddTasksAndDepartmentAsync(fixture, s[0].Id, taskCount: 1);
        await ScopeOfWorkSeeding.AddHeadlessDepartmentAsync(fixture, s[1].Id);
        Assert.True((await AddAsync(p, s[0].Id, s[1].Id)).IsSuccess);
        await SetHeaderAsync(p, durationDays: 10);

        Result<ConfirmScopeOfWorkResponse> result = await ConfirmAsync(p);

        Assert.Equal(TeamErrors.MissingHead, result.Error);
        Assert.Equal(0, await CountStagesAsync(p.ProjectId));
        Assert.Equal(Contracts.ScopeOfWorkStatus.Draft, (await GetAsync(p)).Status);
    }

    [Fact]
    public async Task Each_confirm_rule_is_a_400()
    {
        (_, IReadOnlyList<SeededStage> s) = await ScopeOfWorkSeeding.CreateTemplateAsync(fixture, "Rules", 100m);

        ScopeTestProject noScope = await ScopeOfWorkSeeding.CreateProjectAsync(fixture, "rule-no-scope");
        Assert.Equal(ScopeOfWorkErrors.Empty, (await ConfirmAsync(noScope)).Error);

        ScopeTestProject empty = await ScopeOfWorkSeeding.CreateProjectAsync(fixture, "rule-empty");
        await SetHeaderAsync(empty, durationDays: 10);
        Assert.Equal(ScopeOfWorkErrors.Empty, (await ConfirmAsync(empty)).Error);

        ScopeTestProject noDuration = await ScopeOfWorkSeeding.CreateProjectAsync(fixture, "rule-duration");
        Assert.True((await AddAsync(noDuration, s[0].Id)).IsSuccess);
        Assert.Equal(ScopeOfWorkErrors.DurationRequired, (await ConfirmAsync(noDuration)).Error);

        ScopeTestProject noNote = await ScopeOfWorkSeeding.CreateProjectAsync(fixture, "rule-note");
        Assert.True((await AddAsync(noNote, s[0].Id)).IsSuccess);
        await SetHeaderAsync(noNote, durationDays: 10, adjustment: -10m);
        Assert.Equal(ScopeOfWorkErrors.AdjustmentNoteRequired, (await ConfirmAsync(noNote)).Error);

        ScopeTestProject negative = await ScopeOfWorkSeeding.CreateProjectAsync(fixture, "rule-negative");
        Assert.True((await AddAsync(negative, s[0].Id)).IsSuccess);
        await SetHeaderAsync(negative, durationDays: 10, adjustment: -100.01m, note: "Too much");
        Error negativeError = (await ConfirmAsync(negative)).Error;
        Assert.Equal(ScopeOfWorkErrors.NegativeTotal, negativeError);

        Assert.All(
            [ScopeOfWorkErrors.Empty, ScopeOfWorkErrors.DurationRequired, ScopeOfWorkErrors.AdjustmentNoteRequired, negativeError],
            e => Assert.Equal(400, e.Status));
        Assert.Equal(0, await CountStagesAsync(negative.ProjectId));
    }

    [Fact]
    public async Task After_confirm_every_mutation_is_409_Confirmed()
    {
        ScopeTestProject p = await ScopeOfWorkSeeding.CreateProjectAsync(fixture, "locked");
        (Guid templateId, IReadOnlyList<SeededStage> s) = await ScopeOfWorkSeeding.CreateTemplateAsync(fixture, "Locked", 1m, 2m);
        Guid itemId = (await AddAsync(p, s[0].Id)).Value![0].Id;
        await SetHeaderAsync(p, durationDays: 10);
        Assert.True((await ConfirmAsync(p)).IsSuccess);

        Error[] errors =
        [
            (await SendAsync(p, new UpdateScopeOfWorkHeaderCommand(p.ProjectId, p.OfficeUserId, 20, null, 0m, null, null))).Error,
            (await AddAsync(p, s[1].Id)).Error,
            (await SendAsync<AddScopeOfWorkTemplateCommand, IReadOnlyList<ScopeOfWorkItemResponse>>(
                p, new AddScopeOfWorkTemplateCommand(p.ProjectId, p.OfficeUserId, templateId))).Error,
            (await SendAsync(p, new UpdateScopeOfWorkItemCommand(p.ProjectId, p.OfficeUserId, itemId, "X", null, 5m))).Error,
            (await SendAsync(p, new RemoveScopeOfWorkItemCommand(p.ProjectId, p.OfficeUserId, itemId))).Error,
            (await SendAsync(p, new ReorderScopeOfWorkItemsCommand(p.ProjectId, p.OfficeUserId, [itemId]))).Error,
            (await ConfirmAsync(p)).Error,
        ];

        Assert.All(errors, e => Assert.Equal(ScopeOfWorkErrors.Confirmed, e));
        Assert.Equal(1, await CountStagesAsync(p.ProjectId));
    }

    [Fact]
    public async Task Two_parallel_confirms_confirm_once_create_the_stages_once_and_publish_once()
    {
        for (int round = 0; round < 3; round++)
        {
            ScopeTestProject p = await ScopeOfWorkSeeding.CreateProjectAsync(fixture, $"race-{round}");
            (_, IReadOnlyList<SeededStage> s) = await ScopeOfWorkSeeding.CreateTemplateAsync(fixture, "Race", 1m, 2m);
            Assert.True((await AddAsync(p, s[0].Id, s[1].Id)).IsSuccess);
            await SetHeaderAsync(p, durationDays: 10);

            Result<ConfirmScopeOfWorkResponse>[] results = await Task.WhenAll(ConfirmAsync(p), ConfirmAsync(p));

            Assert.Single(results, r => r.IsSuccess);
            Result<ConfirmScopeOfWorkResponse> loser = Assert.Single(results, r => r.IsFailure);
            Assert.True(loser.Error == ScopeOfWorkErrors.Confirmed || loser.Error == ScopeOfWorkErrors.ConcurrentUpdate,
                loser.Error.ToString());
            Assert.Equal(409, loser.Error.Status);
            Assert.Equal(2, await CountStagesAsync(p.ProjectId));
            Assert.Single(await ConfirmedEventsAsync(p.ProjectId));
        }
    }

    [Fact]
    public async Task The_confirmed_event_carries_ids_and_primitives_only()
    {
        Assert.All(typeof(ScopeOfWorkConfirmedIntegrationEvent).GetProperties(), property =>
            Assert.Contains(property.PropertyType, new[] { typeof(Guid), typeof(decimal), typeof(int), typeof(DateTimeOffset) }));

        ScopeTestProject p = await ScopeOfWorkSeeding.CreateProjectAsync(fixture, "event");
        (_, IReadOnlyList<SeededStage> s) = await ScopeOfWorkSeeding.CreateTemplateAsync(fixture, "Event", 1000m, 500m);
        Assert.True((await AddAsync(p, s[0].Id, s[1].Id)).IsSuccess);
        await SetHeaderAsync(p, durationDays: 30, adjustment: -250m, note: "Discount");
        Assert.True((await ConfirmAsync(p)).IsSuccess);

        ScopeOfWorkConfirmedIntegrationEvent? payload = JsonSerializer.Deserialize<ScopeOfWorkConfirmedIntegrationEvent>(
            Assert.Single(await ConfirmedEventsAsync(p.ProjectId)), WebJson);
        Assert.NotNull(payload);
        Assert.Equal((await GetAsync(p)).Id, payload!.ScopeOfWorkId);
        Assert.Equal(p.ProjectId, payload.ProjectId);
        Assert.Equal(1250m, payload.TotalFees);
        Assert.Equal(30, payload.DurationDays);
    }

    [Fact]
    public async Task A_freestyle_project_is_treated_as_template_instantiation_treats_it()
    {
        (Guid templateId, IReadOnlyList<SeededStage> s) = await ScopeOfWorkSeeding.CreateTemplateAsync(fixture, "Free", 1m);
        await ScopeOfWorkSeeding.AddTasksAndDepartmentAsync(fixture, s[0].Id, taskCount: 1);

        // The template path refuses a staged task in a FreeStyle project ...
        await using (AsyncServiceScope scope = fixture.CreateScope())
        {
            IServiceProvider sp = scope.ServiceProvider;
            (_, string userId) = await HrTestSeeding.HireEmployeeAsync(sp, $"free-{Guid.NewGuid():N}@example.com");
            sp.GetRequiredService<TestCurrentUser>().UserId = userId;
            Result<Guid> fromTemplate = await sp.GetRequiredService<ICommandHandler<CreateProjectCommand, Guid>>()
                .HandleAsync(new CreateProjectCommand(
                    $"FREE-{Guid.NewGuid():N}", "Free", "1 Free St", WorkflowType.FreeStyle,
                    await CrmTestSeeding.SeedClientAsync(sp), StageTemplateId: templateId));
            Assert.Equal(TaskErrors.StageNotAllowed, fromTemplate.Error);
        }

        // ... and so does the confirm, with the same error and nothing created.
        ScopeTestProject p = await ScopeOfWorkSeeding.CreateProjectAsync(fixture, "free", WorkflowType.FreeStyle);
        Assert.True((await AddAsync(p, s[0].Id)).IsSuccess);
        await SetHeaderAsync(p, durationDays: 10);

        Assert.Equal(TaskErrors.StageNotAllowed, (await ConfirmAsync(p)).Error);
        Assert.Equal(0, await CountStagesAsync(p.ProjectId));
    }

    [Fact]
    public async Task CreateProject_from_a_template_still_creates_the_same_skeleton()
    {
        (Guid templateId, IReadOnlyList<SeededStage> s) = await ScopeOfWorkSeeding.CreateTemplateAsync(fixture, "Regress", 900m, null);
        Guid departmentId = await ScopeOfWorkSeeding.AddTasksAndDepartmentAsync(fixture, s[0].Id, taskCount: 2);

        ScopeTestProject p = await ScopeOfWorkSeeding.CreateProjectAsync(fixture, "regress", stageTemplateId: templateId);

        await using AsyncServiceScope read = fixture.CreateScope();
        ProjectsDbContext db = read.ServiceProvider.GetRequiredService<ProjectsDbContext>();
        List<ProjectStage> stages = await db.ProjectStages.AsNoTracking()
            .Where(x => x.ProjectId == p.ProjectId).OrderBy(x => x.Order).ToListAsync();
        Assert.Equal([s[0].Name, s[1].Name], stages.Select(x => x.Name));
        Assert.Equal([1, 2], stages.Select(x => x.Order));
        Assert.Equal([s[0].Id, s[1].Id], stages.Select(x => x.StageId!.Value));
        Assert.All(stages, x => Assert.Null(x.Cost)); // a template carries no fee onto the project
        Assert.Equal(2, stages[0].TotalTasks);
        Assert.Equal(2, await db.ProjectTasks.CountAsync(t => t.ProjectId == p.ProjectId));
        Assert.Equal(departmentId, (await db.StageTeams.AsNoTracking().SingleAsync(t => t.ProjectStageId == stages[0].Id)).DepartmentId);
    }

    // --- helpers ---

    private Task<Result<IReadOnlyList<ScopeOfWorkItemResponse>>> AddAsync(ScopeTestProject p, params Guid[] stageIds) =>
        SendAsync<AddScopeOfWorkItemsCommand, IReadOnlyList<ScopeOfWorkItemResponse>>(
            p, new AddScopeOfWorkItemsCommand(p.ProjectId, p.OfficeUserId, stageIds));

    private async Task SetHeaderAsync(
        ScopeTestProject p, int? durationDays, DateOnly? plannedStart = null, decimal adjustment = 0m, string? note = null)
    {
        Result result = await SendAsync(p, new UpdateScopeOfWorkHeaderCommand(
            p.ProjectId, p.OfficeUserId, durationDays, plannedStart, adjustment, note, null));
        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.ToString() : null);
    }

    private Task<Result<ConfirmScopeOfWorkResponse>> ConfirmAsync(ScopeTestProject p) =>
        SendAsync<ConfirmScopeOfWorkCommand, ConfirmScopeOfWorkResponse>(p, new ConfirmScopeOfWorkCommand(p.ProjectId, p.OfficeUserId));

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

    private async Task<ProjectResponse> GetProjectAsync(ScopeTestProject p)
    {
        await using AsyncServiceScope scope = ScopeOfWorkSeeding.OfficeScope(fixture, p);
        Result<ProjectResponse> result = await scope.ServiceProvider
            .GetRequiredService<IQueryHandler<GetProjectQuery, ProjectResponse>>()
            .HandleAsync(new GetProjectQuery(p.ProjectId, p.OfficeUserId));
        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.ToString() : null);
        return result.Value!;
    }

    private async Task<int> CountStagesAsync(Guid projectId)
    {
        await using AsyncServiceScope scope = fixture.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<ProjectsDbContext>().ProjectStages.CountAsync(s => s.ProjectId == projectId);
    }

    /// <summary>The payloads of this project's <see cref="ScopeOfWorkConfirmedIntegrationEvent"/> outbox rows.</summary>
    private async Task<List<string>> ConfirmedEventsAsync(Guid projectId)
    {
        await using AsyncServiceScope scope = fixture.CreateScope();
        List<string> contents = await scope.ServiceProvider.GetRequiredService<ProjectsDbContext>().Set<OutboxMessage>().AsNoTracking()
            .Where(m => m.Type.Contains(nameof(ScopeOfWorkConfirmedIntegrationEvent)))
            .Select(m => m.Content)
            .ToListAsync();
        return [.. contents.Where(c => c.Contains(projectId.ToString(), StringComparison.OrdinalIgnoreCase))];
    }
}
