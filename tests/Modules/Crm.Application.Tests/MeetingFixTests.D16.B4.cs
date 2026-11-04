using Crm.Application.Features.Jobs;
using Crm.Contracts;
using Crm.Infrastructure.Persistence;
using Crm.Infrastructure.Services;
using Hr.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Notifications.Contracts;
using Projects.Contracts;
using Xunit;

namespace Crm.Application.Tests;

/// <summary>D-16 / B-F4: a recipient lookup that fails for a batch is logged and that batch is left for the
/// next run — the run itself completes instead of throwing.</summary>
public sealed partial class MeetingFixTests
{
    [Fact]
    public async Task D16_B4_AFailingRecipientLookup_DoesNotEndTheRun_AndTheMeetingIsRemindedNextRun()
    {
        MeetingProject m = await SeedAsync("d16-b4");
        Guid meetingId = await ScheduleWithinADayAsync(m, 6);

        await using (AsyncServiceScope scope = fixture.CreateScope())
        {
            IServiceProvider sp = scope.ServiceProvider;
            var job = new CrmMeetingReminderJob(
                sp.GetRequiredService<CrmDbContext>(), sp.GetRequiredService<IHrDirectory>(),
                new FailingClientDirectory(sp.GetRequiredService<IClientDirectory>()), sp.GetRequiredService<IProjectDirectory>(),
                sp.GetRequiredService<INotificationService>(),
                TimeProvider.System, sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<OfficeHoursOptions>>(),
                NullLogger<CrmMeetingReminderJob>.Instance);

            await job.RunAsync(CancellationToken.None);
        }

        Assert.Null((await LoadMeetingAsync(meetingId)).ReminderSentAtUtc);

        await RunReminderJobAsync();
        Assert.NotNull((await LoadMeetingAsync(meetingId)).ReminderSentAtUtc);
    }

    /// <summary>The batch client lookup throws; everything else delegates to the real directory.</summary>
    private sealed class FailingClientDirectory(IClientDirectory inner) : IClientDirectory
    {
        public Task<ClientSummary?> GetAsync(Guid clientId, CancellationToken ct) => inner.GetAsync(clientId, ct);

        public Task<IReadOnlyList<ClientSummary>> GetManyAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct) =>
            throw new InvalidOperationException("Simulated recipient lookup failure.");

        public Task<IReadOnlyList<Guid>> ExistAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct) => inner.ExistAsync(ids, ct);

        public Task<IReadOnlyList<Guid>> ExistActiveAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct) => inner.ExistActiveAsync(ids, ct);

        public Task<ClientSummary?> ResolveByAppUserIdAsync(string appUserId, CancellationToken ct) => inner.ResolveByAppUserIdAsync(appUserId, ct);

        public Task<ClientDisplayName?> GetDisplayNameAsync(Guid clientId, CancellationToken ct) => inner.GetDisplayNameAsync(clientId, ct);

        public Task<IReadOnlyList<ClientDisplayName>> GetDisplayNamesAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct) =>
            inner.GetDisplayNamesAsync(ids, ct);
    }
}
