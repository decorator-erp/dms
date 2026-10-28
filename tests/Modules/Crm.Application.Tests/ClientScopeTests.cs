using BuildingBlocks.Messaging;
using Crm.Contracts;
using Crm.Contracts.Contacts;
using Crm.Contracts.Notes;
using Microsoft.Extensions.DependencyInjection;
using Projects.Contracts;
using Projects.Contracts.Projects;
using SharedKernel;
using Xunit;
using ContactRole = Crm.Contracts.ContactRole;

namespace Crm.Application.Tests;

/// <summary>
/// PR C of the CRM backend completion: clients, contacts and notes. One partial file per row; this file
/// holds the shared seeding. Every message runs in its own scope, as each HTTP request would. An "office"
/// caller holds <c>crm.clients.read_all</c>; every other caller holds only what the test grants.
/// </summary>
[Collection(CrmApplicationCollection.Name)]
public sealed partial class ClientScopeTests(CrmApplicationFixture fixture)
{
    /// <summary>A client with a project, a primary contact and one note written by the project owner.</summary>
    private sealed record ClientFixture(BriefTestProject P, Guid ContactId, Guid NoteId);

    private async Task<ClientFixture> SeedAsync(string label, Guid? departmentHeadEmployeeId = null)
    {
        BriefTestProject p;
        await using (AsyncServiceScope scope = fixture.CreateScope())
        {
            p = await BriefTestSeeding.CreateProjectWithStageAsync(scope.ServiceProvider, label, 1, departmentHeadEmployeeId);
        }

        string office = CrmTestAccess.OfficeUserId;
        Result<Guid> contact = await CommandFor<CreateCrmContactCommand, Guid>(office,
            new CreateCrmContactCommand(p.ClientId, "Jane", "Client", ContactRole.Owner, IsPrimary: true, office), Office);
        Assert.True(contact.IsSuccess, contact.Error?.Description);
        Result<Guid> note = await CommandFor<CreateCrmClientNoteCommand, Guid>(office,
            new CreateCrmClientNoteCommand(p.ClientId, null, "Prefers warm tones", p.EmployeeId, office), Office);
        Assert.True(note.IsSuccess, note.Error?.Description);
        return new ClientFixture(p, contact.Value, note.Value);
    }

    private async Task<(Guid ClientId, string ClientAppUserId)> SeedClientWithoutProjectAsync(string label)
    {
        await using AsyncServiceScope scope = fixture.CreateScope();
        return await CrmClientSeeding.CreateClientAsync(scope.ServiceProvider, $"{label}-{Guid.NewGuid():N}@example.com", lastName: label);
    }

    private async Task<Guid> CreateProjectForAsync(Guid clientId, string ownerAppUserId)
    {
        await using AsyncServiceScope scope = ScopeFor(ownerAppUserId, ProjectsPermissions.Projects.ReadAll);
        Result<Guid> project = await scope.ServiceProvider.GetRequiredService<ICommandHandler<CreateProjectCommand, Guid>>()
            .HandleAsync(new CreateProjectCommand($"C-{Guid.NewGuid():N}"[..12], "Later project", "9 Later St", WorkflowType.FreeStyle, clientId));
        Assert.True(project.IsSuccess, project.Error?.Description);
        return project.Value;
    }

    private async Task<(Guid EmployeeId, string AppUserId)> HireEmployeeAsync(string label)
    {
        await using AsyncServiceScope scope = fixture.CreateScope();
        return await HrTestSeeding.HireEmployeeAsync(
            scope.ServiceProvider, $"{label}-{Guid.NewGuid():N}@example.com", jobTitle: "Designer", firstName: label);
    }

    private static readonly string[] Office = [CrmPermissions.Clients.ReadAll];

    private AsyncServiceScope ScopeFor(string userId, params string[] grants)
    {
        AsyncServiceScope scope = fixture.CreateScope();
        TestCurrentUser currentUser = scope.ServiceProvider.GetRequiredService<TestCurrentUser>();
        currentUser.UserId = userId;
        foreach (string grant in grants)
        {
            currentUser.Grant(grant);
        }

        return scope;
    }

    private async Task<Result> Command<TCommand>(string userId, TCommand command, params string[] grants)
        where TCommand : ICommand
    {
        await using AsyncServiceScope scope = ScopeFor(userId, grants);
        return await scope.ServiceProvider.GetRequiredService<ICommandHandler<TCommand>>().HandleAsync(command);
    }

    private async Task<Result<TResponse>> CommandFor<TCommand, TResponse>(string userId, TCommand command, params string[] grants)
        where TCommand : ICommand<TResponse>
    {
        await using AsyncServiceScope scope = ScopeFor(userId, grants);
        return await scope.ServiceProvider.GetRequiredService<ICommandHandler<TCommand, TResponse>>().HandleAsync(command);
    }

    private async Task<Result<TResponse>> QueryFor<TQuery, TResponse>(string userId, TQuery query, params string[] grants)
        where TQuery : IQuery<TResponse>
    {
        await using AsyncServiceScope scope = ScopeFor(userId, grants);
        return await scope.ServiceProvider.GetRequiredService<IQueryHandler<TQuery, TResponse>>().HandleAsync(query);
    }

    private static Result Plain<T>(Result<T> result) => result.IsSuccess ? Result.Success() : result.Error;

    private static void AssertError(Result result, int status, string? code = null)
    {
        Assert.True(result.IsFailure, $"Expected a {status}, got success.");
        Assert.Equal(status, result.Error.Status);
        if (code is not null)
        {
            Assert.Equal(code, result.Error.Code);
        }
    }

    private static void AssertOk(Result result) => Assert.True(result.IsSuccess, result.Error?.Description);
}
