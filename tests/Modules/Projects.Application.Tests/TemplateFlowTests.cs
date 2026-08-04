using BuildingBlocks.Messaging;
using Microsoft.Extensions.DependencyInjection;
using Projects.Contracts;
using Projects.Contracts.Templates;
using Xunit;

namespace Projects.Application.Tests;

[Collection(ProjectsApplicationCollection.Name)]
public sealed class TemplateFlowTests(ProjectsApplicationFixture fixture)
{
    /// <summary>G5-06's own verify block: template authoring round-trip; template list/detail
    /// queries.</summary>
    [Fact]
    public async Task CreateTemplate_then_addStage_addStageTask_addStageSubTask_roundtrips()
    {
        await using var scope = fixture.CreateScope();
        var sp = scope.ServiceProvider;

        var createTemplate = sp.GetRequiredService<ICommandHandler<CreateStageTemplateCommand, Guid>>();
        var templateResult = await createTemplate.HandleAsync(
            new CreateStageTemplateCommand("Residential Build", StageTemplateType.Custom, IsDefault: true));
        Assert.True(templateResult.IsSuccess);
        var templateId = templateResult.Value;

        var createStage = sp.GetRequiredService<ICommandHandler<CreateStageCommand, Guid>>();
        var stageResult = await createStage.HandleAsync(new CreateStageCommand(templateId, "Design", 1, 25m));
        Assert.True(stageResult.IsSuccess);
        var stageId = stageResult.Value;

        var createTask = sp.GetRequiredService<ICommandHandler<CreateStageTaskCommand, Guid>>();
        var taskResult = await createTask.HandleAsync(new CreateStageTaskCommand(stageId, "Draft floor plan", 1));
        Assert.True(taskResult.IsSuccess);
        var stageTaskId = taskResult.Value;

        var createSubTask = sp.GetRequiredService<ICommandHandler<CreateStageSubTaskCommand, Guid>>();
        var subTaskResult = await createSubTask.HandleAsync(new CreateStageSubTaskCommand(stageTaskId, "Client sign-off", 1));
        Assert.True(subTaskResult.IsSuccess);

        var getTemplate = sp.GetRequiredService<IQueryHandler<GetStageTemplateQuery, StageTemplateResponse>>();
        var template = await getTemplate.HandleAsync(new GetStageTemplateQuery(templateId));
        Assert.True(template.IsSuccess);
        Assert.Equal("Residential Build", template.Value!.Name);

        var getStages = sp.GetRequiredService<IQueryHandler<GetStagesByTemplateQuery, IReadOnlyList<StageResponse>>>();
        var stages = await getStages.HandleAsync(new GetStagesByTemplateQuery(templateId));
        Assert.True(stages.IsSuccess);
        Assert.Single(stages.Value!);
        Assert.Equal("Design", stages.Value![0].Name);

        var getTasks = sp.GetRequiredService<IQueryHandler<GetStageTasksByStageQuery, IReadOnlyList<StageTaskResponse>>>();
        var tasks = await getTasks.HandleAsync(new GetStageTasksByStageQuery(stageId));
        Assert.True(tasks.IsSuccess);
        Assert.Single(tasks.Value!);

        var getSubTasks = sp.GetRequiredService<IQueryHandler<GetStageSubTasksByStageTaskQuery, IReadOnlyList<StageSubTaskResponse>>>();
        var subTasks = await getSubTasks.HandleAsync(new GetStageSubTasksByStageTaskQuery(stageTaskId));
        Assert.True(subTasks.IsSuccess);
        Assert.Single(subTasks.Value!);
    }

    [Fact]
    public async Task GetStageTemplatesQuery_ListsAllTemplates()
    {
        await using var scope = fixture.CreateScope();
        var sp = scope.ServiceProvider;

        var createTemplate = sp.GetRequiredService<ICommandHandler<CreateStageTemplateCommand, Guid>>();
        var name = $"Template-{Guid.NewGuid():N}";
        var result = await createTemplate.HandleAsync(new CreateStageTemplateCommand(name, StageTemplateType.Custom));
        Assert.True(result.IsSuccess);

        var getList = sp.GetRequiredService<IQueryHandler<GetStageTemplatesQuery, IReadOnlyList<StageTemplateResponse>>>();
        var list = await getList.HandleAsync(new GetStageTemplatesQuery());

        Assert.True(list.IsSuccess);
        Assert.Contains(list.Value!, t => t.Name == name);
    }

    [Fact]
    public async Task UpdateThenDeleteStageTemplate_RemovesIt()
    {
        await using var scope = fixture.CreateScope();
        var sp = scope.ServiceProvider;

        var createTemplate = sp.GetRequiredService<ICommandHandler<CreateStageTemplateCommand, Guid>>();
        var templateId = (await createTemplate.HandleAsync(
            new CreateStageTemplateCommand("Temp", StageTemplateType.Custom))).Value;

        var update = sp.GetRequiredService<ICommandHandler<UpdateStageTemplateCommand>>();
        var updateResult = await update.HandleAsync(new UpdateStageTemplateCommand(templateId, Name: "Renamed"));
        Assert.True(updateResult.IsSuccess);

        var getTemplate = sp.GetRequiredService<IQueryHandler<GetStageTemplateQuery, StageTemplateResponse>>();
        var afterUpdate = await getTemplate.HandleAsync(new GetStageTemplateQuery(templateId));
        Assert.Equal("Renamed", afterUpdate.Value!.Name);

        var delete = sp.GetRequiredService<ICommandHandler<DeleteStageTemplateCommand>>();
        var deleteResult = await delete.HandleAsync(new DeleteStageTemplateCommand(templateId));
        Assert.True(deleteResult.IsSuccess);

        var afterDelete = await getTemplate.HandleAsync(new GetStageTemplateQuery(templateId));
        Assert.True(afterDelete.IsFailure);
    }
}
