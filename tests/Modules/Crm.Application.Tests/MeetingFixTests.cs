using BuildingBlocks.Messaging;
using Crm.Contracts;
using Crm.Contracts.Contacts;
using Crm.Contracts.Meetings;
using Crm.Domain;
using Crm.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Projects.Contracts;
using SharedKernel;
using Xunit;
using ContactRole = Crm.Contracts.ContactRole;
using MeetingType = Crm.Contracts.MeetingType;

namespace Crm.Application.Tests;

/// <summary>
/// PR B of the CRM backend completion: meetings. One partial file per row group; this file holds the
/// shared seeding. Every message runs in its own scope, as each HTTP request would. Project owners act
/// with <c>project.read_all</c>; other employees and the client act with no permission.
/// </summary>
[Collection(CrmApplicationCollection.Name)]
public sealed partial class MeetingFixTests(CrmApplicationFixture fixture)
{
    private readonly HashSet<string> _owners = [];

    private sealed record MeetingProject(BriefTestProject P, Guid ContactId);

    private async Task<MeetingProject> SeedAsync(string label, Guid? departmentHeadEmployeeId = null)
    {
        BriefTestProject p;
        await using (AsyncServiceScope scope = fixture.CreateScope())
        {
            p = await BriefTestSeeding.CreateProjectWithStageAsync(scope.ServiceProvider, label, 1, departmentHeadEmployeeId);
        }

        _owners.Add(p.OwnerAppUserId);
        await using AsyncServiceScope contactScope = fixture.CreateScope();
        Result<Guid> contact = await contactScope.ServiceProvider.GetRequiredService<ICommandHandler<CreateCrmContactCommand, Guid>>()
            .HandleAsync(new CreateCrmContactCommand(p.ClientId, "Jane", "Client", ContactRole.Owner, IsPrimary: true));
        Assert.True(contact.IsSuccess, contact.Error?.Description);
        return new MeetingProject(p, contact.Value);
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

    private static DateTime At(int days, int hour = 10) => DateTime.UtcNow.Date.AddDays(days).AddHours(hour);

    private static CrmMeetingSlotRequest Slot(int days, int hour = 10, int minutes = 60) =>
        new(At(days, hour), At(days, hour).AddMinutes(minutes));

    private async Task<Result<Guid>> TryProposeAsync(MeetingProject m, params CrmMeetingSlotRequest[] slots) =>
        await CommandFor<ProposeCrmMeetingCommand, Guid>(m.P.OwnerAppUserId,
            new ProposeCrmMeetingCommand(m.P.ProjectId, m.P.EmployeeId, "Kickoff", MeetingType.InOffice, slots));

    private async Task<Guid> ProposeAsync(MeetingProject m, params CrmMeetingSlotRequest[] slots)
    {
        Result<Guid> proposed = await TryProposeAsync(m, slots.Length == 0 ? [Slot(1), Slot(2)] : slots);
        Assert.True(proposed.IsSuccess, proposed.Error?.Description);
        return proposed.Value;
    }

    private async Task<List<CrmMeetingSlot>> LoadSlotsAsync(Guid meetingId)
    {
        await using AsyncServiceScope scope = fixture.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<CrmDbContext>().CrmMeetingSlots.AsNoTracking()
            .Where(s => s.MeetingId == meetingId).OrderBy(s => s.StartsAtUtc).ToListAsync();
    }

    private async Task<CrmMeeting> LoadMeetingAsync(Guid meetingId)
    {
        await using AsyncServiceScope scope = fixture.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<CrmDbContext>().CrmMeetings.AsNoTracking().SingleAsync(x => x.Id == meetingId);
    }

    private async Task DrainOutboxAsync()
    {
        await fixture.OutboxProcessor.ProcessPendingAsync(CancellationToken.None);
        await fixture.OutboxProcessor.ProcessPendingAsync(CancellationToken.None);
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
