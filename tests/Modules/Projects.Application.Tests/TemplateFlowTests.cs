using BuildingBlocks.Messaging;
using Microsoft.Extensions.DependencyInjection;
using Projects.Contracts;
using Projects.Contracts.Templates;
using SharedKernel;
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
        await using AsyncServiceScope scope = fixture.CreateScope();
        IServiceProvider sp = scope.ServiceProvider;

        ICommandHandler<CreateStageTemplateCommand, Guid> createTemplate = sp.GetRequiredService<ICommandHandler<CreateStageTemplateCommand, Guid>>();
        Result<Guid> templateResult = await createTemplate.HandleAsync(
            new CreateStageTemplateCommand("Residential Build", StageTemplateType.Custom, IsDefault: true));
        Assert.True(templateResult.IsSuccess);
        Guid templateId = templateResult.Value;

        ICommandHandler<CreateStageCommand, Guid> createStage = sp.GetRequiredService<ICommandHandler<CreateStageCommand, Guid>>();
        Result<Guid> stageResult = await createStage.HandleAsync(new CreateStageCommand(templateId, "Design", 1, 25m));
        Assert.True(stageResult.IsSuccess);
        Guid stageId = stageResult.Value;

        ICommandHandler<CreateStageTaskCommand, Guid> createTask = sp.GetRequiredService<ICommandHandler<CreateStageTaskCommand, Guid>>();
        Result<Guid> taskResult = await createTask.HandleAsync(new CreateStageTaskCommand(stageId, "Draft floor plan", 1));
        Assert.True(taskResult.IsSuccess);
        Guid stageTaskId = taskResult.Value;

        ICommandHandler<CreateStageSubTaskCommand, Guid> createSubTask = sp.GetRequiredService<ICommandHandler<CreateStageSubTaskCommand, Guid>>();
        Result<Guid> subTaskResult = await createSubTask.HandleAsync(new CreateStageSubTaskCommand(stageTaskId, "Client sign-off", 1));
        Assert.True(subTaskResult.IsSuccess);

        IQueryHandler<GetStageTemplateQuery, StageTemplateResponse> getTemplate = sp.GetRequiredService<IQueryHandler<GetStageTemplateQuery, StageTemplateResponse>>();
        Result<StageTemplateResponse> template = await getTemplate.HandleAsync(new GetStageTemplateQuery(templateId));
        Assert.True(template.IsSuccess);
        Assert.Equal("Residential Build", template.Value!.Name);

        IQueryHandler<GetStagesByTemplateQuery, IReadOnlyList<StageResponse>> getStages = sp.GetRequiredService<IQueryHandler<GetStagesByTemplateQuery, IReadOnlyList<StageResponse>>>();
        Result<IReadOnlyList<StageResponse>> stages = await getStages.HandleAsync(new GetStagesByTemplateQuery(templateId));
        Assert.True(stages.IsSuccess);
        Assert.Single(stages.Value!);
        Assert.Equal("Design", stages.Value![0].Name);

        IQueryHandler<GetStageTasksByStageQuery, IReadOnlyList<StageTaskResponse>> getTasks = sp.GetRequiredService<IQueryHandler<GetStageTasksByStageQuery, IReadOnlyList<StageTaskResponse>>>();
        Result<IReadOnlyList<StageTaskResponse>> tasks = await getTasks.HandleAsync(new GetStageTasksByStageQuery(stageId));
        Assert.True(tasks.IsSuccess);
        Assert.Single(tasks.Value!);

        IQueryHandler<GetStageSubTasksByStageTaskQuery, IReadOnlyList<StageSubTaskResponse>> getSubTasks = sp.GetRequiredService<IQueryHandler<GetStageSubTasksByStageTaskQuery, IReadOnlyList<StageSubTaskResponse>>>();
        Result<IReadOnlyList<StageSubTaskResponse>> subTasks = await getSubTasks.HandleAsync(new GetStageSubTasksByStageTaskQuery(stageTaskId));
        Assert.True(subTasks.IsSuccess);
        Assert.Single(subTasks.Value!);
    }

    [Fact]
    public async Task GetStageTemplatesQuery_ListsAllTemplates()
    {
        await using AsyncServiceScope scope = fixture.CreateScope();
        IServiceProvider sp = scope.ServiceProvider;

        ICommandHandler<CreateStageTemplateCommand, Guid> createTemplate = sp.GetRequiredService<ICommandHandler<CreateStageTemplateCommand, Guid>>();
        string name = $"Template-{Guid.NewGuid():N}";
        Result<Guid> result = await createTemplate.HandleAsync(new CreateStageTemplateCommand(name, StageTemplateType.Custom));
        Assert.True(result.IsSuccess);

        IQueryHandler<GetStageTemplatesQuery, IReadOnlyList<StageTemplateResponse>> getList = sp.GetRequiredService<IQueryHandler<GetStageTemplatesQuery, IReadOnlyList<StageTemplateResponse>>>();
        Result<IReadOnlyList<StageTemplateResponse>> list = await getList.HandleAsync(new GetStageTemplatesQuery());

        Assert.True(list.IsSuccess);
        Assert.Contains(list.Value!, t => t.Name == name);
    }

    [Fact]
    public async Task UpdateThenDeleteStageTemplate_RemovesIt()
    {
        await using AsyncServiceScope scope = fixture.CreateScope();
        IServiceProvider sp = scope.ServiceProvider;

        ICommandHandler<CreateStageTemplateCommand, Guid> createTemplate = sp.GetRequiredService<ICommandHandler<CreateStageTemplateCommand, Guid>>();
        Guid templateId = (await createTemplate.HandleAsync(
            new CreateStageTemplateCommand("Temp", StageTemplateType.Custom))).Value;

        ICommandHandler<UpdateStageTemplateCommand> update = sp.GetRequiredService<ICommandHandler<UpdateStageTemplateCommand>>();
        Result updateResult = await update.HandleAsync(new UpdateStageTemplateCommand(templateId, Name: "Renamed"));
        Assert.True(updateResult.IsSuccess);

        IQueryHandler<GetStageTemplateQuery, StageTemplateResponse> getTemplate = sp.GetRequiredService<IQueryHandler<GetStageTemplateQuery, StageTemplateResponse>>();
        Result<StageTemplateResponse> afterUpdate = await getTemplate.HandleAsync(new GetStageTemplateQuery(templateId));
        Assert.Equal("Renamed", afterUpdate.Value!.Name);

        ICommandHandler<DeleteStageTemplateCommand> delete = sp.GetRequiredService<ICommandHandler<DeleteStageTemplateCommand>>();
        Result deleteResult = await delete.HandleAsync(new DeleteStageTemplateCommand(templateId));
        Assert.True(deleteResult.IsSuccess);

        Result<StageTemplateResponse> afterDelete = await getTemplate.HandleAsync(new GetStageTemplateQuery(templateId));
        Assert.True(afterDelete.IsFailure);
    }
}
