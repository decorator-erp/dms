using BuildingBlocks.Messaging;
using Documents.Application.Features.Documents;
using Documents.Contracts.Folders;
using Documents.Domain;
using Documents.Infrastructure.Persistence;
using Hr.Contracts.Departments;
using Hr.Contracts.Employees;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Projects.Contracts;
using Projects.Contracts.Projects;
using Projects.Contracts.Stages;
using Projects.Contracts.Tasks;
using Projects.Contracts.Templates;
using SharedKernel;
using Xunit;

namespace Documents.Application.Tests;

/// <summary>
/// G7-06 verify: folder tree CRUD, move/rename, default-folder creation for a real stage and a real
/// task. A real Project/ProjectStage/ProjectTask, not fabricated ids — no manual stage/task creation
/// command exists in Projects.Contracts (only template instantiation), so this is the same
/// department → template → project flow <c>ProjectFlowTests</c> (Projects.Application.Tests) uses.
/// </summary>
[Collection(DocumentsApplicationCollection.Name)]
public sealed class FolderFlowTests(DocumentsApplicationFixture fixture)
{
    [Fact]
    public async Task CreateRenameMoveDelete_FolderTree_RoundTrips()
    {
        await using AsyncServiceScope scope = fixture.CreateScope();
        IServiceProvider sp = scope.ServiceProvider;
        (Guid projectId, Guid _, Guid _) = await CreateProjectWithStageAndTaskAsync(sp);

        ICommandHandler<CreateDocumentFolderCommand, Guid> createRoot = sp.GetRequiredService<ICommandHandler<CreateDocumentFolderCommand, Guid>>();
        Result<Guid> rootResult = await createRoot.HandleAsync(new CreateDocumentFolderCommand(projectId, "Drawings"));
        Assert.True(rootResult.IsSuccess);
        Guid rootId = rootResult.Value;

        ICommandHandler<CreateDocumentFolderCommand, Guid> createChild = sp.GetRequiredService<ICommandHandler<CreateDocumentFolderCommand, Guid>>();
        Result<Guid> childResult = await createChild.HandleAsync(
            new CreateDocumentFolderCommand(projectId, "Structural", ParentFolderId: rootId));
        Assert.True(childResult.IsSuccess);
        Guid childId = childResult.Value;

        IQueryHandler<GetDocumentFolderChildrenQuery, IReadOnlyList<DocumentFolderResponse>> getChildren = sp.GetRequiredService<IQueryHandler<GetDocumentFolderChildrenQuery, IReadOnlyList<DocumentFolderResponse>>>();
        Result<IReadOnlyList<DocumentFolderResponse>> children = await getChildren.HandleAsync(new GetDocumentFolderChildrenQuery(rootId));
        Assert.True(children.IsSuccess);
        Assert.Single(children.Value!, f => f.Id == childId);

        ICommandHandler<RenameDocumentFolderCommand> rename = sp.GetRequiredService<ICommandHandler<RenameDocumentFolderCommand>>();
        Result renameResult = await rename.HandleAsync(new RenameDocumentFolderCommand(childId, "Structural Drawings"));
        Assert.True(renameResult.IsSuccess);

        IQueryHandler<GetDocumentFoldersByProjectQuery, IReadOnlyList<DocumentFolderResponse>> getByProject = sp.GetRequiredService<IQueryHandler<GetDocumentFoldersByProjectQuery, IReadOnlyList<DocumentFolderResponse>>>();
        Result<IReadOnlyList<DocumentFolderResponse>> allFolders = await getByProject.HandleAsync(new GetDocumentFoldersByProjectQuery(projectId));
        Assert.True(allFolders.IsSuccess);
        Assert.Contains(allFolders.Value!, f => f.Id == childId && f.Name == "Structural Drawings");

        ICommandHandler<CreateDocumentFolderCommand, Guid> createSecondRoot = sp.GetRequiredService<ICommandHandler<CreateDocumentFolderCommand, Guid>>();
        Result<Guid> secondRootResult = await createSecondRoot.HandleAsync(new CreateDocumentFolderCommand(projectId, "Archive"));
        Assert.True(secondRootResult.IsSuccess);
        Guid secondRootId = secondRootResult.Value;

        ICommandHandler<MoveDocumentFolderCommand> move = sp.GetRequiredService<ICommandHandler<MoveDocumentFolderCommand>>();
        Result moveResult = await move.HandleAsync(new MoveDocumentFolderCommand(childId, secondRootId));
        Assert.True(moveResult.IsSuccess);

        Result<IReadOnlyList<DocumentFolderResponse>> childrenOfSecondRoot = await getChildren.HandleAsync(new GetDocumentFolderChildrenQuery(secondRootId));
        Assert.Single(childrenOfSecondRoot.Value!, f => f.Id == childId);

        ICommandHandler<DeleteDocumentFolderCommand> delete = sp.GetRequiredService<ICommandHandler<DeleteDocumentFolderCommand>>();
        Result deleteResult = await delete.HandleAsync(new DeleteDocumentFolderCommand(childId));
        Assert.True(deleteResult.IsSuccess);

        DocumentsDbContext db = sp.GetRequiredService<DocumentsDbContext>();
        DocumentFolder deletedFolder = await db.DocumentFolders.AsNoTracking().SingleAsync(f => f.Id == childId);
        Assert.False(deletedFolder.IsActive);
    }

    [Fact]
    public async Task MoveFolder_UnderItsOwnDescendant_IsRejected_ThroughTheRealCommandPath()
    {
        await using AsyncServiceScope scope = fixture.CreateScope();
        IServiceProvider sp = scope.ServiceProvider;
        (Guid projectId, Guid _, Guid _) = await CreateProjectWithStageAndTaskAsync(sp);

        ICommandHandler<CreateDocumentFolderCommand, Guid> create = sp.GetRequiredService<ICommandHandler<CreateDocumentFolderCommand, Guid>>();
        Guid aId = (await create.HandleAsync(new CreateDocumentFolderCommand(projectId, "A"))).Value;
        Guid bId = (await create.HandleAsync(new CreateDocumentFolderCommand(projectId, "B", ParentFolderId: aId))).Value;

        ICommandHandler<MoveDocumentFolderCommand> move = sp.GetRequiredService<ICommandHandler<MoveDocumentFolderCommand>>();
        Result result = await move.HandleAsync(new MoveDocumentFolderCommand(aId, bId));

        Assert.True(result.IsFailure);
        Assert.Equal("document_folders.CyclicMove", result.Error.Code);
    }

    [Fact]
    public async Task CreateStageFolder_ForARealStage_Succeeds_AndCreateTaskFolder_ForARealTask_Succeeds()
    {
        await using AsyncServiceScope scope = fixture.CreateScope();
        IServiceProvider sp = scope.ServiceProvider;
        (Guid _, Guid stageId, Guid taskId) = await CreateProjectWithStageAndTaskAsync(sp);

        ICommandHandler<CreateStageFolderCommand, Guid> createStageFolder = sp.GetRequiredService<ICommandHandler<CreateStageFolderCommand, Guid>>();
        Result<Guid> stageFolderResult = await createStageFolder.HandleAsync(new CreateStageFolderCommand(stageId, "Stage Docs"));
        Assert.True(stageFolderResult.IsSuccess);

        ICommandHandler<CreateTaskFolderCommand, Guid> createTaskFolder = sp.GetRequiredService<ICommandHandler<CreateTaskFolderCommand, Guid>>();
        Result<Guid> taskFolderResult = await createTaskFolder.HandleAsync(new CreateTaskFolderCommand(taskId, "Task Docs"));
        Assert.True(taskFolderResult.IsSuccess);

        IQueryHandler<GetDocumentFoldersByStageQuery, IReadOnlyList<DocumentFolderResponse>> getByStage = sp.GetRequiredService<IQueryHandler<GetDocumentFoldersByStageQuery, IReadOnlyList<DocumentFolderResponse>>>();
        Result<IReadOnlyList<DocumentFolderResponse>> stageFolders = await getByStage.HandleAsync(new GetDocumentFoldersByStageQuery(stageId));
        Assert.Single(stageFolders.Value!, f => f.Id == stageFolderResult.Value);

        IQueryHandler<GetDocumentFoldersByTaskQuery, IReadOnlyList<DocumentFolderResponse>> getByTask = sp.GetRequiredService<IQueryHandler<GetDocumentFoldersByTaskQuery, IReadOnlyList<DocumentFolderResponse>>>();
        Result<IReadOnlyList<DocumentFolderResponse>> taskFolders = await getByTask.HandleAsync(new GetDocumentFoldersByTaskQuery(taskId));
        Assert.Single(taskFolders.Value!, f => f.Id == taskFolderResult.Value);
    }

    [Fact]
    public async Task DefaultFolderResolver_ForAStage_CreatesOnce_AndReturnsTheSameFolderOnASecondCall()
    {
        await using AsyncServiceScope scope = fixture.CreateScope();
        IServiceProvider sp = scope.ServiceProvider;
        (Guid projectId, Guid stageId, Guid _) = await CreateProjectWithStageAndTaskAsync(sp);
        DocumentsDbContext db = sp.GetRequiredService<DocumentsDbContext>();

        Guid firstId = await DefaultFolderResolver.EnsureForStageAsync(db, projectId, stageId, "Design", CancellationToken.None);
        await db.SaveChangesAsync();

        Guid secondId = await DefaultFolderResolver.EnsureForStageAsync(db, projectId, stageId, "Design", CancellationToken.None);

        Assert.Equal(firstId, secondId);

        DocumentFolder folder = await db.DocumentFolders.AsNoTracking().SingleAsync(f => f.Id == firstId);
        Assert.True(folder.IsDefault);
        Assert.Equal(stageId, folder.ProjectStageId);
    }

    [Fact]
    public async Task DefaultFolderResolver_ForATask_CreatesOnce_AndReturnsTheSameFolderOnASecondCall()
    {
        await using AsyncServiceScope scope = fixture.CreateScope();
        IServiceProvider sp = scope.ServiceProvider;
        (Guid projectId, Guid _, Guid taskId) = await CreateProjectWithStageAndTaskAsync(sp);
        DocumentsDbContext db = sp.GetRequiredService<DocumentsDbContext>();

        Guid firstId = await DefaultFolderResolver.EnsureForTaskAsync(db, projectId, taskId, "Draft plan", CancellationToken.None);
        await db.SaveChangesAsync();

        Guid secondId = await DefaultFolderResolver.EnsureForTaskAsync(db, projectId, taskId, "Draft plan", CancellationToken.None);

        Assert.Equal(firstId, secondId);
    }

    /// <summary>Two different tasks whose display names collide must not produce colliding default
    /// folder names - G7-02's correction 5, verified end-to-end here (not just the naming logic in
    /// isolation) by actually persisting both and confirming distinct rows with distinct names. Uses
    /// two REAL tasks (both literally named "Draft plan" in the same stage template), not a fabricated
    /// second id - project_documents/document_folders' own FK to projects.project_tasks (G7-04) rejects
    /// an id with no matching row, exactly as it should.</summary>
    [Fact]
    public async Task DefaultFolderResolver_TwoDifferentTasksWithTheSameDisplayName_ProduceDistinctFolders()
    {
        await using AsyncServiceScope scope = fixture.CreateScope();
        IServiceProvider sp = scope.ServiceProvider;
        (Guid projectId, Guid taskId, Guid otherTaskId) = await CreateProjectWithTwoIdenticallyNamedTasksAsync(sp);
        DocumentsDbContext db = sp.GetRequiredService<DocumentsDbContext>();

        Guid firstId = await DefaultFolderResolver.EnsureForTaskAsync(db, projectId, taskId, "Draft plan", CancellationToken.None);
        Guid secondId = await DefaultFolderResolver.EnsureForTaskAsync(db, projectId, otherTaskId, "Draft plan", CancellationToken.None);
        await db.SaveChangesAsync();

        Assert.NotEqual(firstId, secondId);
        List<string> names = await db.DocumentFolders.AsNoTracking()
            .Where(f => f.Id == firstId || f.Id == secondId)
            .Select(f => f.Name)
            .ToListAsync();
        Assert.Equal(2, names.Distinct().Count());
    }

    /// <summary>One stage template, two stage-tasks both literally named "Draft plan" - instantiating
    /// the template produces two real <c>ProjectTask</c> rows sharing a display name, the exact
    /// collision scenario <c>DefaultFolderResolver</c> must disambiguate.</summary>
    private static async Task<(Guid ProjectId, Guid TaskId, Guid OtherTaskId)> CreateProjectWithTwoIdenticallyNamedTasksAsync(
        IServiceProvider sp)
    {
        TestCurrentUser currentUser = sp.GetRequiredService<TestCurrentUser>();
        (string? ownerUserId, Guid _) = await RegisterEmployeeAsync(sp, "dup-owner");
        (string _, Guid headEmployeeId) = await RegisterEmployeeAsync(sp, "dup-head");
        currentUser.UserId = ownerUserId;
        currentUser.Grant(ProjectsPermissions.Projects.ReadAll);

        Guid departmentId = await CreateDepartmentWithHeadAsync(sp, headEmployeeId);

        ICommandHandler<CreateStageTemplateCommand, Guid> createTemplate = sp.GetRequiredService<ICommandHandler<CreateStageTemplateCommand, Guid>>();
        Guid templateId = (await createTemplate.HandleAsync(
            new CreateStageTemplateCommand($"Build-{Guid.NewGuid():N}", StageTemplateType.Custom))).Value;

        ICommandHandler<CreateStageCommand, Guid> createStage = sp.GetRequiredService<ICommandHandler<CreateStageCommand, Guid>>();
        Guid templateStageId = (await createStage.HandleAsync(new CreateStageCommand(templateId, "Design", 1, 100m))).Value;

        ICommandHandler<CreateStageTaskCommand, Guid> createStageTask = sp.GetRequiredService<ICommandHandler<CreateStageTaskCommand, Guid>>();
        Assert.True((await createStageTask.HandleAsync(new CreateStageTaskCommand(templateStageId, "Draft plan", 1))).IsSuccess);
        Assert.True((await createStageTask.HandleAsync(new CreateStageTaskCommand(templateStageId, "Draft plan", 2))).IsSuccess);

        ICommandHandler<AddStageDepartmentCommand> addStageDepartment = sp.GetRequiredService<ICommandHandler<AddStageDepartmentCommand>>();
        Assert.True((await addStageDepartment.HandleAsync(new AddStageDepartmentCommand(templateStageId, departmentId))).IsSuccess);

        Guid clientId = await CrmTestSeeding.SeedClientAsync(sp);
        ICommandHandler<CreateProjectCommand, Guid> createProject = sp.GetRequiredService<ICommandHandler<CreateProjectCommand, Guid>>();
        Result<Guid> projectResult = await createProject.HandleAsync(new CreateProjectCommand(
            $"P-{Guid.NewGuid():N}", "New Build", "123 Main St", WorkflowType.Stage, clientId,
            StageTemplateId: templateId));
        Assert.True(projectResult.IsSuccess);
        Guid projectId = projectResult.Value;

        IQueryHandler<GetProjectTasksByProjectQuery, IReadOnlyList<ProjectTaskResponse>> getTasks = sp.GetRequiredService<IQueryHandler<GetProjectTasksByProjectQuery, IReadOnlyList<ProjectTaskResponse>>>();
        Result<IReadOnlyList<ProjectTaskResponse>> tasks = await getTasks.HandleAsync(new GetProjectTasksByProjectQuery(projectId, ownerUserId));
        Assert.True(tasks.IsSuccess);
        Assert.Equal(2, tasks.Value!.Count);

        return (projectId, tasks.Value[0].Id, tasks.Value[1].Id);
    }

    internal static async Task<(Guid ProjectId, Guid StageId, Guid TaskId)> CreateProjectWithStageAndTaskAsync(IServiceProvider sp)
    {
        TestCurrentUser currentUser = sp.GetRequiredService<TestCurrentUser>();

        (string? ownerUserId, Guid _) = await RegisterEmployeeAsync(sp, "doc-owner");
        (string _, Guid headEmployeeId) = await RegisterEmployeeAsync(sp, "doc-head");

        currentUser.UserId = ownerUserId;
        currentUser.Grant(ProjectsPermissions.Projects.ReadAll);

        Guid departmentId = await CreateDepartmentWithHeadAsync(sp, headEmployeeId);
        Guid templateId = await CreateTemplateAsync(sp, departmentId);
        Guid clientId = await CrmTestSeeding.SeedClientAsync(sp);

        ICommandHandler<CreateProjectCommand, Guid> createProject = sp.GetRequiredService<ICommandHandler<CreateProjectCommand, Guid>>();
        Result<Guid> projectResult = await createProject.HandleAsync(new CreateProjectCommand(
            $"P-{Guid.NewGuid():N}", "New Build", "123 Main St", WorkflowType.Stage, clientId,
            StageTemplateId: templateId));
        Assert.True(projectResult.IsSuccess);
        Guid projectId = projectResult.Value;

        IQueryHandler<GetProjectStagesByProjectQuery, IReadOnlyList<ProjectStageResponse>> getStages = sp.GetRequiredService<IQueryHandler<GetProjectStagesByProjectQuery, IReadOnlyList<ProjectStageResponse>>>();
        Result<IReadOnlyList<ProjectStageResponse>> stages = await getStages.HandleAsync(new GetProjectStagesByProjectQuery(projectId, ownerUserId));
        Guid stageId = Assert.Single(stages.Value!).Id;

        IQueryHandler<GetProjectTasksByProjectQuery, IReadOnlyList<ProjectTaskResponse>> getTasks = sp.GetRequiredService<IQueryHandler<GetProjectTasksByProjectQuery, IReadOnlyList<ProjectTaskResponse>>>();
        Result<IReadOnlyList<ProjectTaskResponse>> tasks = await getTasks.HandleAsync(new GetProjectTasksByProjectQuery(projectId, ownerUserId));
        Guid taskId = Assert.Single(tasks.Value!).Id;

        return (projectId, stageId, taskId);
    }

    private static async Task<(string UserId, Guid EmployeeId)> RegisterEmployeeAsync(IServiceProvider sp, string label)
    {
        string suffix = Guid.NewGuid().ToString("N");
        (Guid employeeId, string appUserId) = await HrTestSeeding.HireEmployeeAsync(
            sp, $"{label}-{suffix}@example.com", lastName: label);

        return (appUserId, employeeId);
    }

    private static async Task<Guid> CreateDepartmentWithHeadAsync(IServiceProvider sp, Guid headEmployeeId)
    {
        ICommandHandler<CreateDepartmentCommand, Guid> createDepartment = sp.GetRequiredService<ICommandHandler<CreateDepartmentCommand, Guid>>();
        Guid departmentId = (await createDepartment.HandleAsync(new CreateDepartmentCommand($"Dept-{Guid.NewGuid():N}"))).Value;

        ICommandHandler<AssignDepartmentHeadCommand> assignHead = sp.GetRequiredService<ICommandHandler<AssignDepartmentHeadCommand>>();
        Result assignResult = await assignHead.HandleAsync(new AssignDepartmentHeadCommand(departmentId, headEmployeeId));
        Assert.True(assignResult.IsSuccess);

        return departmentId;
    }

    /// <summary>One stage, one task - the minimal skeleton for a real stage/task id, mirroring
    /// <c>ProjectFlowTests.CreateTemplateAsync</c> minus the subtask/department-team parts Documents
    /// doesn't need.</summary>
    private static async Task<Guid> CreateTemplateAsync(IServiceProvider sp, Guid departmentId)
    {
        ICommandHandler<CreateStageTemplateCommand, Guid> createTemplate = sp.GetRequiredService<ICommandHandler<CreateStageTemplateCommand, Guid>>();
        Guid templateId = (await createTemplate.HandleAsync(
            new CreateStageTemplateCommand($"Build-{Guid.NewGuid():N}", StageTemplateType.Custom))).Value;

        ICommandHandler<CreateStageCommand, Guid> createStage = sp.GetRequiredService<ICommandHandler<CreateStageCommand, Guid>>();
        Result<Guid> stageResult = await createStage.HandleAsync(new CreateStageCommand(templateId, "Design", 1, 100m));
        Assert.True(stageResult.IsSuccess);
        Guid templateStageId = stageResult.Value;

        ICommandHandler<CreateStageTaskCommand, Guid> createStageTask = sp.GetRequiredService<ICommandHandler<CreateStageTaskCommand, Guid>>();
        Result<Guid> stageTaskResult = await createStageTask.HandleAsync(new CreateStageTaskCommand(templateStageId, "Draft plan", 1));
        Assert.True(stageTaskResult.IsSuccess);

        ICommandHandler<AddStageDepartmentCommand> addStageDepartment = sp.GetRequiredService<ICommandHandler<AddStageDepartmentCommand>>();
        Assert.True((await addStageDepartment.HandleAsync(new AddStageDepartmentCommand(templateStageId, departmentId))).IsSuccess);

        return templateId;
    }
}
