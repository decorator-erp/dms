using BuildingBlocks.Messaging;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Projects.Contracts;
using Projects.Contracts.Projects;
using Projects.Infrastructure.Persistence;
using SharedKernel;
using Xunit;

namespace Projects.Application.Tests;

/// <summary>Sites/Projects review, phase 3 (Projects data integrity): one red test per finding.</summary>
[Collection(ProjectsApplicationCollection.Name)]
public sealed class ProjectIntegrityTests(ProjectsApplicationFixture fixture)
{
    /// <summary>P8: a second project with a code already in use is a 409, not a 500 from the unique index.</summary>
    [Fact]
    public async Task P8_ASecondProjectWithTheSameCode_IsCodeTaken()
    {
        ScopeTestProject first = await ScopeOfWorkSeeding.CreateProjectAsync(fixture, "p8-first");
        string code = await CodeOfAsync(first.ProjectId);

        await using AsyncServiceScope scope = ScopeOfWorkSeeding.OfficeScope(fixture, first);
        IServiceProvider sp = scope.ServiceProvider;
        Guid clientId = await CrmTestSeeding.SeedClientAsync(sp);
        Result<Guid> second = await sp.GetRequiredService<ICommandHandler<CreateProjectCommand, Guid>>()
            .HandleAsync(new CreateProjectCommand(code, "Duplicate", "1 Dup St", WorkflowType.Stage, clientId));

        Assert.True(second.IsFailure);
        Assert.Equal("projects.CodeTaken", second.Error.Code);
        Assert.Equal(409, second.Error.Status);
    }

    /// <summary>P8: two creates racing past the pre-check — the unique index violation (IX_projects_Code) is
    /// mapped to the same 409.</summary>
    [Fact]
    public async Task P8_ARacingInsertOfTheSameCode_MapsTheIndexViolationToCodeTaken()
    {
        ScopeTestProject first = await ScopeOfWorkSeeding.CreateProjectAsync(fixture, "p8-race");
        string code = await CodeOfAsync(first.ProjectId);

        await using AsyncServiceScope scope = fixture.CreateScope();
        ProjectsDbContext db = scope.ServiceProvider.GetRequiredService<ProjectsDbContext>();
        Domain.Project existing = await db.Projects.AsNoTracking().SingleAsync(p => p.Id == first.ProjectId);
        db.Projects.Add(Domain.Project.Create(
            code, "Racer", "1 Race St", Domain.WorkflowType.Stage, existing.ClientId, existing.OwnerId));

        Result saved = await ProjectWrites.SaveAsync(db, CancellationToken.None);

        Assert.True(saved.IsFailure);
        Assert.Equal("projects.CodeTaken", saved.Error.Code);
    }

    private async Task<string> CodeOfAsync(Guid projectId)
    {
        await using AsyncServiceScope scope = fixture.CreateScope();
        ProjectsDbContext db = scope.ServiceProvider.GetRequiredService<ProjectsDbContext>();
        return await db.Projects.AsNoTracking().Where(p => p.Id == projectId).Select(p => p.Code).SingleAsync();
    }
}
