using BuildingBlocks.Messaging;
using Crm.Contracts.Clients;
using Microsoft.Extensions.DependencyInjection;
using Projects.Contracts;
using Projects.Contracts.Projects;
using SharedKernel;
using Xunit;

namespace Crm.Application.Tests;

/// <summary>D-15 (C-F3, owner default): a Cancelled project doesn't hide its client. A client whose projects
/// are all Cancelled is pipeline again (visible like a client with no project); a Completed one still counts.</summary>
public sealed partial class ClientScopeTests
{
    [Fact]
    public async Task D15_AClientWhoseOnlyProjectIsCancelled_IsVisibleAgain_ButACompletedProjectStillHidesIt()
    {
        (_, string owner) = await HireEmployeeAsync("d15-owner");
        (_, string outsider) = await HireEmployeeAsync("d15-outsider");
        (Guid cancelledClient, _) = await SeedClientWithoutProjectAsync("d15-cancelled");
        (Guid completedClient, _) = await SeedClientWithoutProjectAsync("d15-completed");
        Guid cancelledProject = await CreateProjectForAsync(cancelledClient, owner);
        Guid completedProject = await CreateProjectForAsync(completedClient, owner);
        await ChangeStatusAsync(owner, cancelledProject, ProjectStatus.Cancelled);
        await ChangeStatusAsync(owner, completedProject, ProjectStatus.InProgress);
        await ChangeStatusAsync(owner, completedProject, ProjectStatus.Completed);

        AssertOk(Plain(await QueryFor<GetClientByIdQuery, ClientResponse>(outsider, new GetClientByIdQuery(cancelledClient, outsider))));
        AssertError(Plain(await QueryFor<GetClientByIdQuery, ClientResponse>(outsider, new GetClientByIdQuery(completedClient, outsider))), 404);
    }

    private async Task ChangeStatusAsync(string userId, Guid projectId, ProjectStatus status)
    {
        await using AsyncServiceScope scope = ScopeFor(userId, ProjectsPermissions.Projects.ReadAll);
        Result changed = await scope.ServiceProvider.GetRequiredService<ICommandHandler<ChangeProjectStatusCommand>>()
            .HandleAsync(new ChangeProjectStatusCommand(projectId, userId, status));
        AssertOk(changed);
    }
}
