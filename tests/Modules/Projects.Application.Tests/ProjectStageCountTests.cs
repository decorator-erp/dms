using BuildingBlocks.Messaging;
using BuildingBlocks.Pagination;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Projects.Application.Features.Projects;
using Projects.Contracts.Projects;
using Projects.Infrastructure.Persistence;
using SharedKernel;
using Xunit;

namespace Projects.Application.Tests;

/// <summary>GAP-3: <see cref="ProjectResponse.StageCount"/> in the single and the list query, each still one
/// SQL statement.</summary>
[Collection(ProjectsApplicationCollection.Name)]
public sealed class ProjectStageCountTests(ProjectsApplicationFixture fixture)
{
    [Fact]
    public async Task StageCount_is_0_without_stages_and_3_after_a_three_stage_template_in_both_queries()
    {
        ScopeTestProject empty = await ScopeOfWorkSeeding.CreateProjectAsync(fixture, "count-empty");
        (Guid templateId, _) = await ScopeOfWorkSeeding.CreateTemplateAsync(fixture, "Count", 1m, 2m, 3m);
        ScopeTestProject three = await ScopeOfWorkSeeding.CreateProjectAsync(fixture, "count-three", stageTemplateId: templateId);

        Assert.Equal(0, (await GetAsync(empty)).StageCount);
        Assert.Equal(3, (await GetAsync(three)).StageCount);

        await using AsyncServiceScope scope = ScopeOfWorkSeeding.OfficeScope(fixture, three);
        Result<PagedResult<ProjectResponse>> list = await scope.ServiceProvider
            .GetRequiredService<IQueryHandler<GetProjectsQuery, PagedResult<ProjectResponse>>>()
            .HandleAsync(new GetProjectsQuery(1, 200, three.OfficeUserId, NameFilter: "Scope count-"));
        Assert.True(list.IsSuccess);
        Assert.Equal(0, Assert.Single(list.Value!.Items, x => x.Id == empty.ProjectId).StageCount);
        Assert.Equal(3, Assert.Single(list.Value!.Items, x => x.Id == three.ProjectId).StageCount);
    }

    [Fact]
    public async Task The_stage_count_is_a_subquery_of_one_statement_not_a_query_per_project()
    {
        await using AsyncServiceScope scope = fixture.CreateScope();
        ProjectsDbContext db = scope.ServiceProvider.GetRequiredService<ProjectsDbContext>();

        string single = db.Projects.Where(p => p.Id == Guid.Empty)
            .Select(GetProjectQueryHandler.ToResponse(db.ProjectStages)).ToQueryString();
        string list = db.Projects.OrderBy(p => p.Name).Skip(0).Take(50)
            .Select(GetProjectQueryHandler.ToResponse(db.ProjectStages)).ToQueryString();

        foreach (string sql in new[] { single, list })
        {
            Assert.Contains("count(*)", sql, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("project_stages", sql, StringComparison.Ordinal);
            Assert.Single(
                sql.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
                s => s.Contains("SELECT", StringComparison.OrdinalIgnoreCase));
        }
    }

    private async Task<ProjectResponse> GetAsync(ScopeTestProject p)
    {
        await using AsyncServiceScope scope = ScopeOfWorkSeeding.OfficeScope(fixture, p);
        Result<ProjectResponse> result = await scope.ServiceProvider
            .GetRequiredService<IQueryHandler<GetProjectQuery, ProjectResponse>>()
            .HandleAsync(new GetProjectQuery(p.ProjectId, p.OfficeUserId));
        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.ToString() : null);
        return result.Value!;
    }
}
