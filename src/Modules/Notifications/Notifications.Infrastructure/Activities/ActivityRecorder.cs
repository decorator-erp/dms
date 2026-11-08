using Identity.Contracts;
using Microsoft.EntityFrameworkCore;
using Notifications.Contracts;
using Notifications.Domain;
using Notifications.Infrastructure.Persistence;

namespace Notifications.Infrastructure.Activities;

/// <summary>
/// S11: <see cref="IActivityRecorder"/>. Writes through <see cref="NotificationsDbContext"/>, so when the caller is
/// <see cref="ActivityRecordedIntegrationEventHandler"/> the rows commit in the same transaction as the inbox row that
/// marks the event handled (<c>InboxDedupeDecorator</c>) — the row and the "delivered" mark stand or fall together.
///
/// <para>A fixed number of statements per call, whatever the entry count: one existence read, one batched
/// <see cref="IUserDirectory.GetManyAsync"/> for the actor names nobody supplied, one save.</para>
/// </summary>
internal sealed class ActivityRecorder(NotificationsDbContext db, IUserDirectory users) : IActivityRecorder
{
    public Task RecordAsync(ActivityEntry entry, CancellationToken ct) => RecordManyAsync([entry], ct);

    public async Task RecordManyAsync(IReadOnlyCollection<ActivityEntry> entries, CancellationToken ct)
    {
        List<ActivityEntry> distinct = [.. entries.DistinctBy(e => e.Id)];
        if (distinct.Count == 0)
        {
            return;
        }

        Guid[] ids = [.. distinct.Select(e => e.Id)];
        HashSet<Guid> existing = [.. await db.Activities.AsNoTracking().Where(a => ids.Contains(a.Id)).Select(a => a.Id).ToListAsync(ct)];
        List<ActivityEntry> fresh = [.. distinct.Where(e => !existing.Contains(e.Id))];
        if (fresh.Count == 0)
        {
            return;
        }

        string[] unnamed = [.. fresh
            .Where(e => e.ActorName is null && !string.IsNullOrEmpty(e.ActorUserId))
            .Select(e => e.ActorUserId!)
            .Distinct(StringComparer.Ordinal)];
        Dictionary<string, string?> names = unnamed.Length == 0
            ? []
            : (await users.GetManyAsync(unnamed, ct)).ToDictionary(u => u.Id, u => u.DisplayName, StringComparer.Ordinal);

        foreach (ActivityEntry e in fresh)
        {
            string? actorName = e.ActorName
                ?? (e.ActorUserId is { } actor ? names.GetValueOrDefault(actor) : null);
            db.Activities.Add(Activity.Create(
                e.Id, (ActivityAction)(int)e.Action, e.EntityName, e.EntityId, e.Message, e.OccurredAtUtc,
                e.ActorUserId, Truncate(actorName, 256), e.ProjectId, Truncate(e.EntityDisplayName, 256)));
        }

        await db.SaveChangesAsync(ct);
    }

    private static string? Truncate(string? value, int max) =>
        value is null || value.Length <= max ? value : value[..max];
}
