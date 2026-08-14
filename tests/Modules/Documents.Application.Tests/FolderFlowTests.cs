using BuildingBlocks.Messaging;
using Documents.Application.Features.Documents;
using Documents.Contracts.Folders;
using Documents.Infrastructure.Persistence;
using Hr.Contracts.Departments;
using Hr.Contracts.Employees;
using Identity.Contracts.Auth;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Projects.Contracts;
using Projects.Contracts.Projects;
using Projects.Contracts.Stages;
using Projects.Contracts.Tasks;
using Projects.Contracts.Templates;
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
        await using var scope = fixture.CreateScope();
        var sp = scope.ServiceProvider;
        var (projectId, _, _) = await CreateProjectWithStageAndTaskAsync(sp);

        var createRoot = sp.GetRequiredService<ICommandHandler<CreateDocumentFolderCommand, Guid>>();
        var rootResult = await createRoot.HandleAsync(new CreateDocumentFolderCommand(projectId, "Drawings"));
        Assert.True(rootResult.IsSuccess);
        var rootId = rootResult.Value;

        var createChild = sp.GetRequiredService<ICommandHandler<CreateDocumentFolderCommand, Guid>>();
        var childResult = await createChild.HandleAsync(
            new CreateDocumentFolderCommand(projectId, "Structural", ParentFolderId: rootId));
        Assert.True(childResult.IsSuccess);
        var childId = childResult.Value;

        var getChildren = sp.GetRequiredService<IQueryHandler<GetDocumentFolderChildrenQuery, IReadOnlyList<DocumentFolderResponse>>>();
        var children = await getChildren.HandleAsync(new GetDocumentFolderChildrenQuery(rootId));
        Assert.True(children.IsSuccess);
        Assert.Single(children.Value!, f => f.Id == childId);

        var rename = sp.GetRequiredService<ICommandHandler<RenameDocumentFolderCommand>>();
        var renameResult = await rename.HandleAsync(new RenameDocumentFolderCommand(childId, "Structural Drawings"));
        Assert.True(renameResult.IsSuccess);

        var getByProject = sp.GetRequiredService<IQueryHandler<GetDocumentFoldersByProjectQuery, IReadOnlyList<DocumentFolderResponse>>>();
        var allFolders = await getByProject.HandleAsync(new GetDocumentFoldersByProjectQuery(projectId));
        Assert.True(allFolders.IsSuccess);
        Assert.Contains(allFolders.Value!, f => f.Id == childId && f.Name == "Structural Drawings");

        var createSecondRoot = sp.GetRequiredService<ICommandHandler<CreateDocumentFolderCommand, Guid>>();
        var secondRootResult = await createSecondRoot.HandleAsync(new CreateDocumentFolderCommand(projectId, "Archive"));
        Assert.True(secondRootResult.IsSuccess);
        var secondRootId = secondRootResult.Value;

        var move = sp.GetRequiredService<ICommandHandler<MoveDocumentFolderCommand>>();
        var moveResult = await move.HandleAsync(new MoveDocumentFolderCommand(childId, secondRootId));
        Assert.True(moveResult.IsSuccess);

        var childrenOfSecondRoot = await getChildren.HandleAsync(new GetDocumentFolderChildrenQuery(secondRootId));
        Assert.Single(childrenOfSecondRoot.Value!, f => f.Id == childId);

        var delete = sp.GetRequiredService<ICommandHandler<DeleteDocumentFolderCommand>>();
        var deleteResult = await delete.HandleAsync(new DeleteDocumentFolderCommand(childId));
        Assert.True(deleteResult.IsSuccess);

        var db = sp.GetRequiredService<DocumentsDbContext>();
        var deletedFolder = await db.DocumentFolders.AsNoTracking().SingleAsync(f => f.Id == childId);
        Assert.False(deletedFolder.IsActive);
    }

    [Fact]
    public async Task MoveFolder_UnderItsOwnDescendant_IsRejected_ThroughTheRealCommandPath()
    {
        await using var scope = fixture.CreateScope();
        var sp = scope.ServiceProvider;
        var (projectId, _, _) = await CreateProjectWithStageAndTaskAsync(sp);

        var create = sp.GetRequiredService<ICommandHandler<CreateDocumentFolderCommand, Guid>>();
        var aId = (await create.HandleAsync(new CreateDocumentFolderCommand(projectId, "A"))).Value;
        var bId = (await create.HandleAsync(new CreateDocumentFolderCommand(projectId, "B", ParentFolderId: aId))).Value;

        var move = sp.GetRequiredService<ICommandHandler<MoveDocumentFolderCommand>>();
        var result = await move.HandleAsync(new MoveDocumentFolderCommand(aId, bId));

        Assert.True(result.IsFailure);
        Assert.Equal("document_folders.CyclicMove", result.Error.Code);
    }

    [Fact]
    public async Task CreateStageFolder_ForARealStage_Succeeds_AndCreateTaskFolder_ForARealTask_Succeeds()
    {
        await using var scope = fixture.CreateScope();
        var sp = scope.ServiceProvider;
        var (_, stageId, taskId) = await CreateProjectWithStageAndTaskAsync(sp);

        var createStageFolder = sp.GetRequiredService<ICommandHandler<CreateStageFolderCommand, Guid>>();
        var stageFolderResult = await createStageFolder.HandleAsync(new CreateStageFolderCommand(stageId, "Stage Docs"));
        Assert.True(stageFolderResult.IsSuccess);

        var createTaskFolder = sp.GetRequiredService<ICommandHandler<CreateTaskFolderCommand, Guid>>();
        var taskFolderResult = await createTaskFolder.HandleAsync(new CreateTaskFolderCommand(taskId, "Task Docs"));
        Assert.True(taskFolderResult.IsSuccess);

        var getByStage = sp.GetRequiredService<IQueryHandler<GetDocumentFoldersByStageQuery, IReadOnlyList<DocumentFolderResponse>>>();
        var stageFolders = await getByStage.HandleAsync(new GetDocumentFoldersByStageQuery(stageId));
        Assert.Single(stageFolders.Value!, f => f.Id == stageFolderResult.Value);

        var getByTask = sp.GetRequiredService<IQueryHandler<GetDocumentFoldersByTaskQuery, IReadOnlyList<DocumentFolderResponse>>>();
        var taskFolders = await getByTask.HandleAsync(new GetDocumentFoldersByTaskQuery(taskId));
        Assert.Single(taskFolders.Value!, f => f.Id == taskFolderResult.Value);
    }

    [Fact]
    public async Task DefaultFolderResolver_ForAStage_CreatesOnce_AndReturnsTheSameFolderOnASecondCall()
    {
        await using var scope = fixture.CreateScope();
        var sp = scope.ServiceProvider;
        var (projectId, stageId, _) = await CreateProjectWithStageAndTaskAsync(sp);
        var db = sp.GetRequiredService<DocumentsDbContext>();

        var firstId = await DefaultFolderResolver.EnsureForStageAsync(db, projectId, stageId, "Design", CancellationToken.None);
        await db.SaveChangesAsync();

        var secondId = await DefaultFolderResolver.EnsureForStageAsync(db, projectId, stageId, "Design", CancellationToken.None);

        Assert.Equal(firstId, secondId);

        var folder = await db.DocumentFolders.AsNoTracking().SingleAsync(f => f.Id == firstId);
        Assert.True(folder.IsDefault);
        Assert.Equal(stageId, folder.ProjectStageId);
    }

    [Fact]
    public async Task DefaultFolderResolver_ForATask_CreatesOnce_AndReturnsTheSameFolderOnASecondCall()
    {
        await using var scope = fixture.CreateScope();
        var sp = scope.ServiceProvider;
        var (projectId, _, taskId) = await CreateProjectWithStageAndTaskAsync(sp);
        var db = sp.GetRequiredService<DocumentsDbContext>();

        var firstId = await DefaultFolderResolver.EnsureForTaskAsync(db, projectId, taskId, "Draft plan", CancellationToken.None);
        await db.SaveChangesAsync();

        var secondId = await DefaultFolderResolver.EnsureForTaskAsync(db, projectId, taskId, "Draft plan", CancellationToken.None);

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
        await using var scope = fixture.CreateScope();
        var sp = scope.ServiceProvider;
        var (projectId, taskId, otherTaskId) = await CreateProjectWithTwoIdenticallyNamedTasksAsync(sp);
        var db = sp.GetRequiredService<DocumentsDbContext>();

        var firstId = await DefaultFolderResolver.EnsureForTaskAsync(db, projectId, taskId, "Draft plan", CancellationToken.None);
        var secondId = await DefaultFolderResolver.EnsureForTaskAsync(db, projectId, otherTaskId, "Draft plan", CancellationToken.None);
        await db.SaveChangesAsync();

        Assert.NotEqual(firstId, secondId);
        var names = await db.DocumentFolders.AsNoTracking()
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
        var currentUser = sp.GetRequiredService<TestCurrentUser>();
        var (ownerUserId, _) = await RegisterEmployeeAsync(sp, "dup-owner");
        var (_, headEmployeeId) = await RegisterEmployeeAsync(sp, "dup-head");
        currentUser.UserId = ownerUserId;
        currentUser.Grant(ProjectsPermissions.Projects.ReadAll);

        var departmentId = await CreateDepartmentWithHeadAsync(sp, headEmployeeId);

        var createTemplate = sp.GetRequiredService<ICommandHandler<CreateStageTemplateCommand, Guid>>();
        var templateId = (await createTemplate.HandleAsync(
            new CreateStageTemplateCommand($"Build-{Guid.NewGuid():N}", StageTemplateType.Custom))).Value;

        var createStage = sp.GetRequiredService<ICommandHandler<CreateStageCommand, Guid>>();
        var templateStageId = (await createStage.HandleAsync(new CreateStageCommand(templateId, "Design", 1, 100m))).Value;

        var createStageTask = sp.GetRequiredService<ICommandHandler<CreateStageTaskCommand, Guid>>();
        Assert.True((await createStageTask.HandleAsync(new CreateStageTaskCommand(templateStageId, "Draft plan", 1))).IsSuccess);
        Assert.True((await createStageTask.HandleAsync(new CreateStageTaskCommand(templateStageId, "Draft plan", 2))).IsSuccess);

        var addStageDepartment = sp.GetRequiredService<ICommandHandler<AddStageDepartmentCommand>>();
        Assert.True((await addStageDepartment.HandleAsync(new AddStageDepartmentCommand(templateStageId, departmentId))).IsSuccess);

        var clientId = await CrmTestSeeding.SeedClientAsync(sp, ownerUserId);
        var createProject = sp.GetRequiredService<ICommandHandler<CreateProjectCommand, Guid>>();
        var projectResult = await createProject.HandleAsync(new CreateProjectCommand(
            $"P-{Guid.NewGuid():N}", "New Build", "123 Main St", WorkflowType.Stage, clientId,
            StageTemplateId: templateId));
        Assert.True(projectResult.IsSuccess);
        var projectId = projectResult.Value;

        var getTasks = sp.GetRequiredService<IQueryHandler<GetProjectTasksByProjectQuery, IReadOnlyList<ProjectTaskResponse>>>();
        var tasks = await getTasks.HandleAsync(new GetProjectTasksByProjectQuery(projectId, ownerUserId));
        Assert.True(tasks.IsSuccess);
        Assert.Equal(2, tasks.Value!.Count);

        return (projectId, tasks.Value[0].Id, tasks.Value[1].Id);
    }

    internal static async Task<(Guid ProjectId, Guid StageId, Guid TaskId)> CreateProjectWithStageAndTaskAsync(IServiceProvider sp)
    {
        var currentUser = sp.GetRequiredService<TestCurrentUser>();

        var (ownerUserId, _) = await RegisterEmployeeAsync(sp, "doc-owner");
        var (_, headEmployeeId) = await RegisterEmployeeAsync(sp, "doc-head");

        currentUser.UserId = ownerUserId;
        currentUser.Grant(ProjectsPermissions.Projects.ReadAll);

        var departmentId = await CreateDepartmentWithHeadAsync(sp, headEmployeeId);
        var templateId = await CreateTemplateAsync(sp, departmentId);
        var clientId = await CrmTestSeeding.SeedClientAsync(sp, ownerUserId);

        var createProject = sp.GetRequiredService<ICommandHandler<CreateProjectCommand, Guid>>();
        var projectResult = await createProject.HandleAsync(new CreateProjectCommand(
            $"P-{Guid.NewGuid():N}", "New Build", "123 Main St", WorkflowType.Stage, clientId,
            StageTemplateId: templateId));
        Assert.True(projectResult.IsSuccess);
        var projectId = projectResult.Value;

        var getStages = sp.GetRequiredService<IQueryHandler<GetProjectStagesByProjectQuery, IReadOnlyList<ProjectStageResponse>>>();
        var stages = await getStages.HandleAsync(new GetProjectStagesByProjectQuery(projectId, ownerUserId));
        var stageId = Assert.Single(stages.Value!).Id;

        var getTasks = sp.GetRequiredService<IQueryHandler<GetProjectTasksByProjectQuery, IReadOnlyList<ProjectTaskResponse>>>();
        var tasks = await getTasks.HandleAsync(new GetProjectTasksByProjectQuery(projectId, ownerUserId));
        var taskId = Assert.Single(tasks.Value!).Id;

        return (projectId, stageId, taskId);
    }

    private static async Task<(string UserId, Guid EmployeeId)> RegisterEmployeeAsync(IServiceProvider sp, string label)
    {
        var suffix = Guid.NewGuid().ToString("N");
        var register = sp.GetRequiredService<ICommandHandler<RegisterCommand, string>>();
        var registerResult = await register.HandleAsync(new RegisterCommand(
            new RegisterRequest($"{label}-{suffix}@example.com", "P@ssw0rd123!", "Test", label, null, "employee")));
        Assert.True(registerResult.IsSuccess);
        var appUserId = registerResult.Value!;

        var createEmployee = sp.GetRequiredService<ICommandHandler<CreateEmployeeCommand, Guid>>();
        var createResult = await createEmployee.HandleAsync(
            new CreateEmployeeCommand(appUserId, "Engineer", DateOnly.FromDateTime(DateTime.UtcNow), "FullTime"));
        Assert.True(createResult.IsSuccess);

        return (appUserId, createResult.Value);
    }

    private static async Task<Guid> CreateDepartmentWithHeadAsync(IServiceProvider sp, Guid headEmployeeId)
    {
        var createDepartment = sp.GetRequiredService<ICommandHandler<CreateDepartmentCommand, Guid>>();
        var departmentId = (await createDepartment.HandleAsync(new CreateDepartmentCommand($"Dept-{Guid.NewGuid():N}"))).Value;

        var assignHead = sp.GetRequiredService<ICommandHandler<AssignDepartmentHeadCommand>>();
        var assignResult = await assignHead.HandleAsync(new AssignDepartmentHeadCommand(departmentId, headEmployeeId));
        Assert.True(assignResult.IsSuccess);

        return departmentId;
    }

    /// <summary>One stage, one task - the minimal skeleton for a real stage/task id, mirroring
    /// <c>ProjectFlowTests.CreateTemplateAsync</c> minus the subtask/department-team parts Documents
    /// doesn't need.</summary>
    private static async Task<Guid> CreateTemplateAsync(IServiceProvider sp, Guid departmentId)
    {
        var createTemplate = sp.GetRequiredService<ICommandHandler<CreateStageTemplateCommand, Guid>>();
        var templateId = (await createTemplate.HandleAsync(
            new CreateStageTemplateCommand($"Build-{Guid.NewGuid():N}", StageTemplateType.Custom))).Value;

        var createStage = sp.GetRequiredService<ICommandHandler<CreateStageCommand, Guid>>();
        var stageResult = await createStage.HandleAsync(new CreateStageCommand(templateId, "Design", 1, 100m));
        Assert.True(stageResult.IsSuccess);
        var templateStageId = stageResult.Value;

        var createStageTask = sp.GetRequiredService<ICommandHandler<CreateStageTaskCommand, Guid>>();
        var stageTaskResult = await createStageTask.HandleAsync(new CreateStageTaskCommand(templateStageId, "Draft plan", 1));
        Assert.True(stageTaskResult.IsSuccess);

        var addStageDepartment = sp.GetRequiredService<ICommandHandler<AddStageDepartmentCommand>>();
        Assert.True((await addStageDepartment.HandleAsync(new AddStageDepartmentCommand(templateStageId, departmentId))).IsSuccess);

        return templateId;
    }
}
