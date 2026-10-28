using BuildingBlocks.Messaging;
using BuildingBlocks.Outbox;
using BuildingBlocks.Pagination;
using Crm.Contracts;
using Crm.Contracts.Briefs;
using Crm.Contracts.Clients;
using Crm.Contracts.Contacts;
using Crm.Contracts.Portal;
using Crm.Infrastructure.Endpoints;
using Crm.Infrastructure.IntegrationEvents;
using Crm.Infrastructure.Persistence;
using Identity.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using SharedKernel;
using Xunit;

namespace Crm.Application.Tests;

/// <summary>X-3: a deactivated or soft-deleted client loses its portal standing at once, and its portal login
/// is disabled once the outbox runs. Reactivating restores both.</summary>
[Collection(CrmApplicationCollection.Name)]
public sealed class ClientStandingTests(CrmApplicationFixture fixture)
{
    private const string NoStanding = "crm.access.NoStanding";

    [Fact]
    public async Task A_deactivated_clients_portal_user_has_no_standing_on_every_portal_route()
    {
        await using AsyncServiceScope scope = fixture.CreateScope();
        IServiceProvider sp = scope.ServiceProvider;
        BriefTestProject p = await BriefTestSeeding.CreateProjectWithStageAsync(sp, "standing");
        await AddPrimaryContactAsync(sp, p.ClientId);
        Assert.True((await SetActiveAsync(sp, p.ClientId, false)).IsSuccess);

        string user = p.ClientAppUserId;
        Assert.Equal(NoStanding, (await sp.GetRequiredService<IQueryHandler<GetMyPortalBriefQuery, PortalBriefViewResponse>>()
            .HandleAsync(new GetMyPortalBriefQuery(user, p.ProjectId, p.StageId))).Error.Code);
        Assert.Equal(NoStanding, (await sp.GetRequiredService<IQueryHandler<GetMyPortalBriefRoundsQuery, IReadOnlyList<CrmBriefRoundResponse>>>()
            .HandleAsync(new GetMyPortalBriefRoundsQuery(user, Guid.NewGuid()))).Error.Code);
        Assert.Equal(NoStanding, (await sp.GetRequiredService<IQueryHandler<GetMyPortalMeetingsQuery, PagedResult<PortalMeetingResponse>>>()
            .HandleAsync(new GetMyPortalMeetingsQuery(user))).Error.Code);

        // Every portal write (item create, meeting request, slot accept/decline, reschedule, cancel) resolves
        // its contact here before the handler runs.
        Result<Guid> contact = await CrmEndpointActors.ResolveContactIdAsync(sp.GetRequiredService<ICrmAccessGuard>(), user, CancellationToken.None);
        Assert.Equal(NoStanding, contact.Error.Code);
        Assert.Equal(403, contact.Error.Status);
    }

    [Fact]
    public async Task Delete_enqueues_one_ClientDeactivated_event()
    {
        await using AsyncServiceScope scope = fixture.CreateScope();
        IServiceProvider sp = scope.ServiceProvider;
        (Guid clientId, _) = await CreateClientAsync(sp);

        Assert.True((await sp.GetRequiredService<ICommandHandler<DeleteClientCommand>>()
            .HandleAsync(new DeleteClientCommand(clientId, CrmTestAccess.Office(sp)))).IsSuccess);

        Assert.Equal(1, await CountOutboxAsync(nameof(ClientDeactivatedIntegrationEvent), clientId));
        Assert.Equal(0, await CountOutboxAsync(nameof(ClientReactivatedIntegrationEvent), clientId));
    }

    [Fact]
    public async Task SetActive_publishes_only_on_an_actual_change()
    {
        await using AsyncServiceScope scope = fixture.CreateScope();
        IServiceProvider sp = scope.ServiceProvider;
        (Guid clientId, _) = await CreateClientAsync(sp);

        Assert.True((await SetActiveAsync(sp, clientId, true)).IsSuccess);
        Assert.Equal(0, await CountOutboxAsync(nameof(ClientReactivatedIntegrationEvent), clientId));

        Assert.True((await SetActiveAsync(sp, clientId, false)).IsSuccess);
        Assert.True((await SetActiveAsync(sp, clientId, false)).IsSuccess);
        Assert.Equal(1, await CountOutboxAsync(nameof(ClientDeactivatedIntegrationEvent), clientId));

        Assert.True((await SetActiveAsync(sp, clientId, true)).IsSuccess);
        Assert.Equal(1, await CountOutboxAsync(nameof(ClientReactivatedIntegrationEvent), clientId));
    }

    [Fact]
    public async Task The_outbox_disables_a_deactivated_clients_login_and_reenables_it_on_reactivation()
    {
        await using AsyncServiceScope scope = fixture.CreateScope();
        IServiceProvider sp = scope.ServiceProvider;
        (Guid clientId, string appUserId) = await CreateClientAsync(sp);
        IUserDirectory users = sp.GetRequiredService<IUserDirectory>();

        Assert.True((await SetActiveAsync(sp, clientId, false)).IsSuccess);
        await DrainOutboxAsync();
        Assert.False((await users.GetAsync(appUserId, CancellationToken.None))!.IsActive);

        Assert.True((await SetActiveAsync(sp, clientId, true)).IsSuccess);
        await DrainOutboxAsync();
        Assert.True((await users.GetAsync(appUserId, CancellationToken.None))!.IsActive);
    }

    [Fact]
    public async Task The_handlers_call_IUserAdministration_with_the_clients_login()
    {
        await using AsyncServiceScope scope = fixture.CreateScope();
        IServiceProvider sp = scope.ServiceProvider;
        (Guid clientId, string appUserId) = await CreateClientAsync(sp);
        CrmDbContext db = sp.GetRequiredService<CrmDbContext>();
        var admin = new RecordingUserAdministration();

        Assert.True((await SetActiveAsync(sp, clientId, false)).IsSuccess);
        await new ClientDeactivatedIntegrationEventHandler(admin, db).HandleAsync(
            new ClientDeactivatedIntegrationEvent(Guid.NewGuid(), clientId, appUserId, DateTimeOffset.UtcNow), CancellationToken.None);
        Assert.Equal([$"deactivate:{appUserId}"], admin.Calls);

        Assert.True((await SetActiveAsync(sp, clientId, true)).IsSuccess);
        await new ClientReactivatedIntegrationEventHandler(admin, db).HandleAsync(
            new ClientReactivatedIntegrationEvent(Guid.NewGuid(), clientId, appUserId, DateTimeOffset.UtcNow), CancellationToken.None);
        Assert.Equal([$"deactivate:{appUserId}", $"reactivate:{appUserId}"], admin.Calls);
    }

    [Fact]
    public async Task A_stale_event_never_overrides_the_clients_current_state()
    {
        await using AsyncServiceScope scope = fixture.CreateScope();
        IServiceProvider sp = scope.ServiceProvider;
        (Guid clientId, string appUserId) = await CreateClientAsync(sp);
        CrmDbContext db = sp.GetRequiredService<CrmDbContext>();
        var admin = new RecordingUserAdministration();

        // Active now: a late deactivate does nothing.
        await new ClientDeactivatedIntegrationEventHandler(admin, db).HandleAsync(
            new ClientDeactivatedIntegrationEvent(Guid.NewGuid(), clientId, appUserId, DateTimeOffset.UtcNow), CancellationToken.None);

        // Inactive now: a late reactivate, or a late ClientCreated (A1), does nothing either.
        Assert.True((await SetActiveAsync(sp, clientId, false)).IsSuccess);
        await new ClientReactivatedIntegrationEventHandler(admin, db).HandleAsync(
            new ClientReactivatedIntegrationEvent(Guid.NewGuid(), clientId, appUserId, DateTimeOffset.UtcNow), CancellationToken.None);
        await new ClientCreatedIntegrationEventHandler(admin, db, NullLogger<ClientCreatedIntegrationEventHandler>.Instance).HandleAsync(
            new ClientCreatedIntegrationEvent(Guid.NewGuid(), clientId, appUserId, DateTimeOffset.UtcNow), CancellationToken.None);

        Assert.Empty(admin.Calls);
    }

    /// <summary>A1: a client created and deleted before its activation is delivered keeps a disabled login.</summary>
    [Fact]
    public async Task A_client_deleted_before_activation_is_delivered_stays_locked_out()
    {
        await using AsyncServiceScope scope = fixture.CreateScope();
        IServiceProvider sp = scope.ServiceProvider;
        Result<Guid> created = await sp.GetRequiredService<ICommandHandler<CreateClientCommand, Guid>>()
            .HandleAsync(new CreateClientCommand(
                $"race-{Guid.NewGuid():N}@example.com", IdentityTestSeeding.Password, "Race", "Client", "1 Race St"));
        Assert.True(created.IsSuccess, created.IsFailure ? created.Error.ToString() : null);
        string appUserId = (await sp.GetRequiredService<IClientDirectory>().GetAsync(created.Value, CancellationToken.None))!.AppUserId;

        Assert.True((await sp.GetRequiredService<ICommandHandler<DeleteClientCommand>>()
            .HandleAsync(new DeleteClientCommand(created.Value, CrmTestAccess.Office(sp)))).IsSuccess);
        await DrainOutboxAsync();

        Assert.False((await sp.GetRequiredService<IUserDirectory>().GetAsync(appUserId, CancellationToken.None))!.IsActive);
    }

    private static Task<(Guid ClientId, string AppUserId)> CreateClientAsync(IServiceProvider sp) =>
        CrmClientSeeding.CreateClientAsync(sp, $"standing-{Guid.NewGuid():N}@example.com");

    private static Task<Result> SetActiveAsync(IServiceProvider sp, Guid clientId, bool isActive) =>
        sp.GetRequiredService<ICommandHandler<SetClientActiveCommand>>().HandleAsync(new SetClientActiveCommand(clientId, isActive, CrmTestAccess.Office(sp)));

    private static async Task AddPrimaryContactAsync(IServiceProvider sp, Guid clientId)
    {
        Result<Guid> contact = await sp.GetRequiredService<ICommandHandler<CreateCrmContactCommand, Guid>>()
            .HandleAsync(new CreateCrmContactCommand(clientId, "Jane", "Doe", ContactRole.Owner, IsPrimary: true, CrmTestAccess.Office(sp)));
        Assert.True(contact.IsSuccess, contact.IsFailure ? contact.Error.ToString() : null);
    }

    private async Task DrainOutboxAsync()
    {
        await fixture.OutboxProcessor.ProcessPendingAsync(CancellationToken.None);
        await fixture.OutboxProcessor.ProcessPendingAsync(CancellationToken.None);
    }

    private async Task<int> CountOutboxAsync(string eventTypeName, Guid clientId)
    {
        await using AsyncServiceScope scope = fixture.CreateScope();
        List<string> contents = await scope.ServiceProvider.GetRequiredService<SharedDbContext>()
            .OutboxMessages.AsNoTracking()
            .Where(m => m.Type.Contains(eventTypeName))
            .Select(m => m.Content)
            .ToListAsync();
        return contents.Count(c => c.Contains(clientId.ToString(), StringComparison.OrdinalIgnoreCase));
    }

    private sealed class RecordingUserAdministration : IUserAdministration
    {
        public List<string> Calls { get; } = [];

        public Task<Result> DeactivateAsync(string userId, CancellationToken ct)
        {
            Calls.Add($"deactivate:{userId}");
            return Task.FromResult(Result.Success());
        }

        public Task<Result> ReactivateAsync(string userId, CancellationToken ct)
        {
            Calls.Add($"reactivate:{userId}");
            return Task.FromResult(Result.Success());
        }

        public Task<Result> UpdateNameAsync(string userId, string firstName, string lastName, CancellationToken ct) =>
            Task.FromResult(Result.Success());
    }
}
