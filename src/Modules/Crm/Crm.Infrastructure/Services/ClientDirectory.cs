using Crm.Contracts;
using Crm.Infrastructure.Persistence;
using Identity.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Crm.Infrastructure.Services;

/// <summary><see cref="IClientDirectory"/>'s implementation — projects straight to the Contracts DTO
/// via <c>Select</c>, never load-then-map, matching <c>DocumentQueries</c>'s own precedent. First
/// caller is <c>Projects.Application</c>'s <c>CreateProjectCommandHandler</c> (G9-13), which validates
/// <c>Project.ClientId</c> through <see cref="ExistAsync"/> — closing the Phase 2 deferral.</summary>
internal sealed class ClientDirectory(CrmDbContext db, IUserDirectory users) : IClientDirectory
{
    public Task<ClientSummary?> GetAsync(Guid clientId, CancellationToken ct) =>
        db.Clients.AsNoTracking()
            .Where(c => c.Id == clientId)
            .Select(c => new ClientSummary(c.Id, c.AppUserId, c.Address, c.RepresentativeName, c.IsActive))
            .FirstOrDefaultAsync(ct);

    public async Task<IReadOnlyList<ClientSummary>> GetManyAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct)
    {
        if (ids.Count == 0)
        {
            return [];
        }

        return await db.Clients.AsNoTracking()
            .Where(c => ids.Contains(c.Id))
            .Select(c => new ClientSummary(c.Id, c.AppUserId, c.Address, c.RepresentativeName, c.IsActive))
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<Guid>> ExistAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct)
    {
        if (ids.Count == 0)
        {
            return [];
        }

        return await db.Clients.AsNoTracking()
            .Where(c => ids.Contains(c.Id))
            .Select(c => c.Id)
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<Guid>> ExistActiveAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct)
    {
        if (ids.Count == 0)
        {
            return [];
        }

        return await db.Clients.AsNoTracking()
            .Where(c => ids.Contains(c.Id) && c.IsActive)
            .Select(c => c.Id)
            .ToListAsync(ct);
    }

    public Task<ClientSummary?> ResolveByAppUserIdAsync(string appUserId, CancellationToken ct) =>
        db.Clients.AsNoTracking()
            .Where(c => c.AppUserId == appUserId)
            .Select(c => new ClientSummary(c.Id, c.AppUserId, c.Address, c.RepresentativeName, c.IsActive))
            .FirstOrDefaultAsync(ct);

    public async Task<ClientDisplayName?> GetDisplayNameAsync(Guid clientId, CancellationToken ct)
    {
        IReadOnlyList<ClientDisplayName> names = await GetDisplayNamesAsync([clientId], ct);
        return names.Count == 0 ? null : names[0];
    }

    /// <summary>Exactly two calls whatever the count: the CRM projection, then one batched
    /// <see cref="IUserDirectory.GetManyAsync"/>, zipped in memory.</summary>
    public async Task<IReadOnlyList<ClientDisplayName>> GetDisplayNamesAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct)
    {
        if (ids.Count == 0)
        {
            return [];
        }

        var clients = await db.Clients.AsNoTracking()
            .Where(c => ids.Contains(c.Id))
            .Select(c => new { c.Id, c.AppUserId })
            .ToListAsync(ct);
        if (clients.Count == 0)
        {
            return [];
        }

        IReadOnlyList<UserSummary> logins = await users.GetManyAsync([.. clients.Select(c => c.AppUserId).Distinct()], ct);
        var names = logins.ToDictionary(u => u.Id, u => u.DisplayName);
        return [.. clients.Select(c => new ClientDisplayName(c.Id, names.GetValueOrDefault(c.AppUserId)))];
    }
}
