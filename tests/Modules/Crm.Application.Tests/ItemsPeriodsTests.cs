using BuildingBlocks.Messaging;
using Crm.Contracts;
using Crm.Contracts.Items;
using Crm.Contracts.Periods;
using Crm.Contracts.Spaces;
using Crm.Domain;
using Crm.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Projects.Contracts;
using SharedKernel;
using Xunit;
using RequirementCategory = Crm.Contracts.RequirementCategory;

namespace Crm.Application.Tests;

/// <summary>
/// PR A of the CRM backend completion: requirement items, periods and spaces. One partial file per row;
/// this file holds the shared seeding. Every message runs in its own scope, as each HTTP request would.
/// Project owners act with <c>project.read_all</c>; every other employee acts with no permission, so
/// their scope decision rests on stage-team membership alone.
/// </summary>
[Collection(CrmApplicationCollection.Name)]
public sealed partial class ItemsPeriodsTests(CrmApplicationFixture fixture)
{
    private readonly HashSet<string> _owners = [];

    private async Task<BriefTestProject> SeedAsync(string label, int stageCount = 1, Guid? departmentHeadEmployeeId = null)
    {
        await using AsyncServiceScope scope = fixture.CreateScope();
        BriefTestProject p = await BriefTestSeeding.CreateProjectWithStageAsync(
            scope.ServiceProvider, label, stageCount, departmentHeadEmployeeId);
        _owners.Add(p.OwnerAppUserId);
        return p;
    }

    private async Task<(Guid EmployeeId, string AppUserId)> HireEmployeeAsync(string label)
    {
        await using AsyncServiceScope scope = fixture.CreateScope();
        return await HrTestSeeding.HireEmployeeAsync(
            scope.ServiceProvider, $"{label}-{Guid.NewGuid():N}@example.com", jobTitle: "Designer", firstName: label);
    }

    private AsyncServiceScope ScopeFor(string userId, bool? readAll = null)
    {
        AsyncServiceScope scope = fixture.CreateScope();
        TestCurrentUser currentUser = scope.ServiceProvider.GetRequiredService<TestCurrentUser>();
        currentUser.UserId = userId;
        if (readAll ?? _owners.Contains(userId))
        {
            currentUser.Grant(ProjectsPermissions.Projects.ReadAll);
        }

        return scope;
    }

    private async Task<Result> Command<TCommand>(string userId, TCommand command, bool? readAll = null)
        where TCommand : ICommand
    {
        await using AsyncServiceScope scope = ScopeFor(userId, readAll);
        return await scope.ServiceProvider.GetRequiredService<ICommandHandler<TCommand>>().HandleAsync(command);
    }

    private async Task<Result<TResponse>> CommandFor<TCommand, TResponse>(string userId, TCommand command, bool? readAll = null)
        where TCommand : ICommand<TResponse>
    {
        await using AsyncServiceScope scope = ScopeFor(userId, readAll);
        return await scope.ServiceProvider.GetRequiredService<ICommandHandler<TCommand, TResponse>>().HandleAsync(command);
    }

    private async Task<Result<TResponse>> QueryFor<TQuery, TResponse>(string userId, TQuery query, bool? readAll = null)
        where TQuery : IQuery<TResponse>
    {
        await using AsyncServiceScope scope = ScopeFor(userId, readAll);
        return await scope.ServiceProvider.GetRequiredService<IQueryHandler<TQuery, TResponse>>().HandleAsync(query);
    }

    private static Result Plain<T>(Result<T> result) => result.IsSuccess ? Result.Success() : result.Error;

    private async Task<Guid> OpenPeriodAsync(BriefTestProject p, Guid? stageId = null)
    {
        Result<Guid> result = await CommandFor<OpenCrmRequirementPeriodCommand, Guid>(p.OwnerAppUserId,
            new OpenCrmRequirementPeriodCommand(p.ProjectId, stageId, DateTime.UtcNow.AddDays(7), p.EmployeeId, p.OwnerAppUserId));
        Assert.True(result.IsSuccess, result.Error?.Description);
        return result.Value;
    }

    private static CreateCrmRequirementItemCommand Item(BriefTestProject p, Guid periodId, string title = "Matte flooring") =>
        new(p.ProjectId, periodId, p.EmployeeId, RequirementCategory.Materials, title, RequirementPriority.High, p.OwnerAppUserId);

    private async Task<Guid> CreateItemAsync(BriefTestProject p, Guid periodId, Guid? projectTaskId = null)
    {
        Result<Guid> result = await CommandFor<CreateCrmRequirementItemCommand, Guid>(
            p.OwnerAppUserId, Item(p, periodId) with { ProjectTaskId = projectTaskId });
        Assert.True(result.IsSuccess, result.Error?.Description);
        return result.Value;
    }

    private async Task<Guid> CreateSpaceTypeAsync(string name = "Room")
    {
        await using AsyncServiceScope scope = fixture.CreateScope();
        Result<Guid> type = await scope.ServiceProvider.GetRequiredService<ICommandHandler<CreateCrmSpaceTypeCommand, Guid>>()
            .HandleAsync(new CreateCrmSpaceTypeCommand($"T{Guid.NewGuid():N}"[..20], name, "غرفة"));
        Assert.True(type.IsSuccess, type.Error?.Description);
        return type.Value;
    }

    private async Task<Guid> CreateSpaceAsync(BriefTestProject p, string name = "Room")
    {
        Guid typeId = await CreateSpaceTypeAsync();
        Result<Guid> space = await CommandFor<CreateCrmSpaceCommand, Guid>(
            p.OwnerAppUserId, new CreateCrmSpaceCommand(p.ProjectId, typeId, name, p.OwnerAppUserId));
        Assert.True(space.IsSuccess, space.Error?.Description);
        return space.Value;
    }

    private async Task<CrmRequirementItem> LoadItemAsync(Guid itemId)
    {
        await using AsyncServiceScope scope = fixture.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<CrmDbContext>()
            .CrmRequirementItems.AsNoTracking().SingleAsync(i => i.Id == itemId);
    }

    private static void AssertError(Result result, int status, string? code = null)
    {
        Assert.True(result.IsFailure, $"Expected a {status}, got success.");
        Assert.Equal(status, result.Error.Status);
        if (code is not null)
        {
            Assert.Equal(code, result.Error.Code);
        }
    }
}
