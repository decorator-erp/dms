using BuildingBlocks.Messaging;
using BuildingBlocks.Pagination;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Projects.Contracts;
using Projects.Contracts.Members;
using Projects.Contracts.Projects;
using Projects.Infrastructure.Persistence;
using SharedKernel;
using Xunit;

namespace Projects.Application.Tests;

/// <summary><c>MyLevel</c>: the caller's own level on the project, on the single get, the paged list and the details,
/// read from <see cref="IProjectAccessLevelReader"/> — the reader the scope decorator enforces with.</summary>
[Collection(ProjectsApplicationCollection.Name)]
public sealed class ProjectMyLevelTests(ProjectsApplicationFixture fixture)
{
    /// <summary>L1: the owner reads Manager and a Viewer member reads Viewer, on all three reads.</summary>
    [Fact]
    public async Task L1_GetListAndDetails_CarryTheCallersLevel()
    {
        string label = Label("l1");
        (ScopeTestProject p, _, _) = await StageProjectWithTasksAsync(label, 1);
        (Guid viewerId, string viewerUser) = await HireAsync("l1-viewer");
        await OfficeOkAsync<AddProjectMemberCommand, Guid>(p, new AddProjectMemberCommand(p.ProjectId, p.OfficeUserId, viewerId, ProjectMemberAccess.Viewer));

        Assert.Equal(Three(ProjectMemberAccess.Manager), await MyLevelsAsync(p.ProjectId, p.OfficeUserId, label));
        Assert.Equal(Three(ProjectMemberAccess.Viewer), await MyLevelsAsync(p.ProjectId, viewerUser, label));
    }

    // ---- helpers ----

    private static string Label(string prefix) => $"{prefix}-{Guid.NewGuid():N}"[..20];

    private static ProjectMemberAccess?[] Three(ProjectMemberAccess? level) => [level, level, level];

    /// <summary>[get, list, details] — null where the read does not return the project.</summary>
    private async Task<ProjectMemberAccess?[]> MyLevelsAsync(Guid projectId, string userId, string label, bool readAll = false)
    {
        await using AsyncServiceScope scope = As(userId, readAll);
        IServiceProvider sp = scope.ServiceProvider;
        Result<ProjectResponse> get = await sp.GetRequiredService<IQueryHandler<GetProjectQuery, ProjectResponse>>()
            .HandleAsync(new GetProjectQuery(projectId, userId));
        Result<PagedResult<ProjectResponse>> list = await sp.GetRequiredService<IQueryHandler<GetProjectsQuery, PagedResult<ProjectResponse>>>()
            .HandleAsync(new GetProjectsQuery(1, 200, userId, NameFilter: $"Scope {label}"));
        Result<ProjectDetailsResponse> details = await sp.GetRequiredService<IQueryHandler<GetProjectDetailsQuery, ProjectDetailsResponse>>()
            .HandleAsync(new GetProjectDetailsQuery(projectId, userId));
        Assert.True(list.IsSuccess, list.IsFailure ? list.Error.ToString() : null);

        return
        [
            get.IsSuccess ? get.Value!.MyLevel : null,
            list.Value!.Items.SingleOrDefault(x => x.Id == projectId)?.MyLevel,
            details.IsSuccess ? details.Value!.MyLevel : null,
        ];
    }

    private async Task<(ScopeTestProject Project, Guid StageId, Guid[] TaskIds)> StageProjectWithTasksAsync(string label, int taskCount)
    {
        (Guid templateId, IReadOnlyList<SeededStage> stages) = await ScopeOfWorkSeeding.CreateTemplateAsync(fixture, label, [null]);
        await ScopeOfWorkSeeding.AddTasksAndDepartmentAsync(fixture, stages[0].Id, taskCount);
        ScopeTestProject p = await ScopeOfWorkSeeding.CreateProjectAsync(fixture, label, stageTemplateId: templateId);

        await using AsyncServiceScope scope = fixture.CreateScope();
        ProjectsDbContext db = scope.ServiceProvider.GetRequiredService<ProjectsDbContext>();
        Guid stageId = await db.ProjectStages.AsNoTracking().Where(s => s.ProjectId == p.ProjectId)
            .OrderBy(s => s.Order).Select(s => s.Id).FirstAsync();
        Guid[] taskIds = await db.ProjectTasks.AsNoTracking().Where(t => t.ProjectStageId == stageId)
            .OrderBy(t => t.Order).Select(t => t.Id).ToArrayAsync();
        return (p, stageId, taskIds);
    }

    private async Task<(Guid EmployeeId, string AppUserId)> HireAsync(string label)
    {
        await using AsyncServiceScope seed = fixture.CreateScope();
        return await HrTestSeeding.HireEmployeeAsync(seed.ServiceProvider, $"{label}-{Guid.NewGuid():N}@example.com");
    }

    private async Task OfficeOkAsync<TCommand, TResponse>(ScopeTestProject p, TCommand command) where TCommand : ICommand<TResponse>
    {
        await using AsyncServiceScope scope = ScopeOfWorkSeeding.OfficeScope(fixture, p);
        Result<TResponse> result = await scope.ServiceProvider.GetRequiredService<ICommandHandler<TCommand, TResponse>>().HandleAsync(command);
        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.ToString() : null);
    }

    private AsyncServiceScope As(string userId, bool readAll = false)
    {
        AsyncServiceScope scope = fixture.CreateScope();
        TestCurrentUser user = scope.ServiceProvider.GetRequiredService<TestCurrentUser>();
        user.UserId = userId;
        if (readAll)
        {
            user.Grant(ProjectsPermissions.Projects.ReadAll);
        }

        return scope;
    }
}
